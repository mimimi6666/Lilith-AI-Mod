using LilithRuntimeContracts;
using System.Text.Json;

namespace LilithRuntime;

// Sole product letters scheduler, replacing (not running alongside) old daily
// model/approval orchestration. Only this host queues an already-reviewed text
// to the existing authenticated game lease and original PNG executor.
internal sealed class LilithLettersCoordinator : ILilithDailyNoteGameHost
{
    private readonly object _sync = new();
    private readonly Func<Func<ILilithLettersHostScope, CancellationToken, Task>, Func<bool>, CancellationToken, Task> _withScope;
    private readonly Func<bool> _ready;
    private readonly TimeProvider _time;
    private readonly Guid _generation;
    private readonly string _home;
    private readonly LilithDailyNoteClock _clock;
    private readonly LilithLettersTiming _timing;
    private string? _lastDiagnostic;
    private readonly CancellationTokenSource _stop = new();
    private LilithLettersStore? _store;
    private LilithLettersSnapshot? _state;
    private LilithDailyNoteGoalSnapshot? _legacy;
    private LilithDailyNoteOpportunity? _opportunity;
    private LilithGameNotesPeerIdentity _peer;
    private DateTimeOffset? _absentSince;
    private DateTimeOffset _nextReceiptCheckUtc;
    private Task? _worker;
    private Work? _work;
    private bool _faulted, _disposed;
    private string _reason = "waiting_for_game";

    internal LilithLettersCoordinator(LilithRuntimeService runtime, LilithRuntimeSnapshot snapshot, LilithGameHermesSessionHost host)
        : this(snapshot.HermesHomePath, snapshot.GenerationId, () => runtime.State == LilithRuntimeState.Ready,
            host.WithLettersScopeAsync)
    {
        host.AttachLettersObserver(Record);
    }

    internal LilithLettersCoordinator(string home, Guid generation, Func<bool> ready,
        Func<Func<ILilithLettersHostScope, CancellationToken, Task>, Func<bool>, CancellationToken, Task> withScope,
        TimeProvider? time = null, Func<TimeZoneInfo>? zone = null, LilithLettersTiming? timing = null)
    {
        _home = home; _generation = generation; _ready = ready; _withScope = withScope;
        _time = time ?? TimeProvider.System; _clock = new(_time, zone);
        _timing = timing ?? LilithLettersTiming.Load(home, _time.GetUtcNow());
    }

    internal object Status()
    {
        lock (_sync)
        {
            Initialize();
            return new { enabled = _state!.Settings.Enabled, daily_limit = _state.Settings.DailyLimit,
                revision = _state.Revision, reason = (_timing.AllowsPresentTest(_time.GetUtcNow()) ? "operator_test|" : _timing.IsTest(_time.GetUtcNow()) ? "test_timing|" : "") + _reason
                    + (_lastDiagnostic is null ? "" : "|" + _lastDiagnostic), pending = _work is not null,
                delivered_today = new LilithLetterQuota(_state.Deliveries.Select(d => d.Receipt)).CountForDay(_time.GetUtcNow(), _clock.TimeZone) + LegacyCount(_time.GetUtcNow()) };
        }
    }

    internal LilithLettersSnapshot Snapshot()
    { lock (_sync) { Initialize(); return _state!; } }

    internal object Configure(bool enabled, int limit)
    {
        lock (_sync)
        {
            Initialize(); Revoke();
            Commit(new LilithLettersCommand.Configure(new(enabled, limit), Now()));
            _reason = enabled ? "waiting_for_conversation" : "disabled";
            return Status();
        }
    }

    private void Initialize()
    {
        if (_faulted || _disposed) throw new InvalidOperationException("Letters service unavailable.");
        if (_store is not null) return;
        try
        {
            // No rewriting old settings/data. Preserve its fulfilled-day budget;
            // an unresolved old write blocks new deliveries instead of resetting.
            if (Directory.Exists(Path.Combine(_home, LilithDailyNoteGoalStore.DirectoryName)))
                _legacy = new LilithDailyNoteGoalStore(_home, LilithDailyNoteProtocol.ProfileId, _clock.TimeZone).Read();
            _store = new(_home, LilithDailyNoteProtocol.ProfileId, _clock.TimeZone, timing: _timing);
            _state = _store.Initialize(hostAuthorizedFirstUse: true);
            Commit(new LilithLettersCommand.Resume(Now()));
        }
        catch { _faulted = true; _reason = "state_unavailable"; throw; }
    }

    private DateTimeOffset Now()
    {
        var reading = _clock.Read(_state?.HighWaterUtc ?? DateTimeOffset.UnixEpoch);
        if (!reading.Trusted) throw new InvalidDataException("Letters clock not trusted.");
        return reading.UtcNow;
    }
    private void Commit(LilithLettersCommand command) => _state = _store!.Apply(_state!, command);
    internal void Record(LilithLetterTurnReference source)
    {
        lock (_sync)
        {
            Initialize(); if (!_state!.Settings.Enabled) return;
            var now = Now(); Commit(new LilithLettersCommand.Prune(now));
            Commit(new LilithLettersCommand.Record(source, now));
            _reason = "accumulating_conversation";
        }
    }
    private static bool SamePeer(LilithGameNotesPeerIdentity a, LilithGameNotesPeerIdentity b)
        => a.ProcessId == b.ProcessId && a.ProcessCreationTime == b.ProcessCreationTime;
    private int LegacyCount(DateTimeOffset now) => _legacy?.LastSatisfiedDay == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _clock.TimeZone).DateTime) ? 1 : 0;
    private bool LegacyUnknown => _legacy?.Current?.Phase is LilithDailyNoteGoalPhase.EvaluationReserved or LilithDailyNoteGoalPhase.DeliveryReserved or LilithDailyNoteGoalPhase.Unknown;
    private bool Current(bool requireAbsent = true)
    {
        var now = _time.GetUtcNow();
        return !_disposed && !_faulted && !_stop.IsCancellationRequested && _state?.Settings.Enabled == true
            && _peer.IsValid && _opportunity is { Enabled: true, Safe: true } o
            && now >= o.ObservedAtUtc && now < o.ExpiresAtUtc
            && (!requireAbsent || o.PlayerAbsent || _timing.AllowsPresentTest(now) || (_state.Batch?.PendingAtRestart == true && !_timing.IsTest(now)) || _work?.AllowPresent == true)
            && _ready();
    }
    private LilithLetterOpportunity Opportunity(DateTimeOffset now) => new(now, true,
        _opportunity!.PlayerAbsent, _absentSince is { } at ? now - at : TimeSpan.Zero, false, true, true);

    public Task<bool> OfferAsync(LilithDailyNoteOpportunity opportunity, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_disposed || _faulted || !peer.IsValid || opportunity.ProfileId != LilithDailyNoteProtocol.ProfileId) return Task.FromResult(false);
            try
            {
                Initialize(); var now = Now();
                if (opportunity.ObservedAtUtc > now || opportunity.ExpiresAtUtc <= now) return Task.FromResult(false);
                if (_opportunity is { } prior)
                {
                    if (SamePeer(peer, _peer) && opportunity.Sequence <= prior.Sequence) return Task.FromResult(false);
                    if (!SamePeer(peer, _peer) || prior.GameEpoch != opportunity.GameEpoch) Revoke();
                    if (!SamePeer(peer, _peer) || prior.ExpiresAtUtc <= now) _absentSince = null;
                }
                _peer = peer; _opportunity = opportunity;
                _absentSince = opportunity.PlayerAbsent ? _absentSince ?? now : null;
                if (_state!.Settings.Enabled) Commit(new LilithLettersCommand.Collect(now));
                if (!Current(false)) { Revoke(); _reason = !_state!.Settings.Enabled ? "disabled" : "awaiting_safe_absence"; return Task.FromResult(true); }
                if (_work?.Command.Action == LilithNoteInboxAction.Deliver && !Current()) Revoke();
                if (_worker is { IsCompleted: false }) return Task.FromResult(true);
                if (_work is not null && _work.Scope.ExpiresAtUtc <= now)
                { Revoke(); _work?.Dispose(); _work = null; }
                if (_work is not null) return Task.FromResult(true);
                Commit(new LilithLettersCommand.Prune(now)); Commit(new LilithLettersCommand.Collect(now));
                var unresolved = _state!.Deliveries.FirstOrDefault(d => d.Receipt.Phase is LilithLetterDeliveryPhase.Reserved or LilithLetterDeliveryPhase.Unknown);
                if (unresolved is not null)
                {
                    if (now >= _nextReceiptCheckUtc) { Queue(unresolved.Receipt.DeliveryId, LilithNoteInboxAction.Status, null, now); _nextReceiptCheckUtc = now.AddMinutes(1); }
                    _reason = "checking_delivery_receipt"; return Task.FromResult(true);
                }
                if (LegacyUnknown) { _reason = "legacy_delivery_unresolved"; return Task.FromResult(true); }
                var quota = new LilithLetterQuota(_state.Deliveries.Select(d => d.Receipt));
                if (quota.CountForDay(now, _clock.TimeZone) + LegacyCount(now) >= _state.Settings.DailyLimit)
                { _reason = "daily_limit_reached"; return Task.FromResult(true); }
                var refs = _state.Turns.Where(t => !t.Used && !t.Withdrawn && t.Kind != LilithLetterConversationKind.ToolOnly).ToArray();
                if (refs.Length == 0) { _reason = "waiting_for_conversation"; return Task.FromResult(true); }
                if (now - refs.Min(t => t.Source.CompletedAtUtc) < _timing.Age(now))
                { _reason = "accumulating_conversation"; return Task.FromResult(true); }
                // Classification may run while the player is present, but only
                // on an idle game lane. This lets the 8-hour batch seal survive
                // shutdown without prematurely delivering a letter.
                var canDraft = _timing.AllowsPresentTest(now) || (_state.Batch?.PendingAtRestart == true && !_timing.IsTest(now)) ||
                    (_absentSince is { } absent && opportunity.PlayerAbsent && now - absent >= _timing.Absence(now));
                if (!canDraft && (_state.Batch is not null || refs.All(t => t.Kind == LilithLetterConversationKind.Conversation)))
                { _reason = "awaiting_safe_absence"; return Task.FromResult(true); }
                if (LilithLettersState.EvaluationWaitReason(_state, now, _clock.TimeZone, _timing) is { } wait)
                { _reason = wait; return Task.FromResult(true); }
                try { Commit(new LilithLettersCommand.BeginPreparation(now)); }
                catch (InvalidOperationException) { _reason = "preparation_backoff"; return Task.FromResult(true); }
                _reason = "preparing";
                Diagnostic("preparation_started", 0);
                _worker = Task.Run(() => RunAsync(canDraft));
                return Task.FromResult(true);
            }
            catch { _faulted = true; _reason = "state_unavailable"; Revoke(); return Task.FromResult(false); }
        }
    }

    private async Task RunAsync(bool canDraft)
    {
        var phase = "scope_acquire";
        var modelCalls = 0;
        try
        {
            await _withScope(async (scope, token) =>
            {
                LilithLettersSnapshot before; string locale;
                lock (_sync) { if (!Current(canDraft)) return; before = _state!; locale = _opportunity!.Locale; }
                phase = "history_initial";
                var history = await scope.ReadAsync(before, token).ConfigureAwait(false);
                DateTimeOffset now;
                lock (_sync)
                {
                    token.ThrowIfCancellationRequested();
                    if (history is null || !scope.IsCurrent || !Current(canDraft) || _state!.Revision != before.Revision)
                    {
                        _reason = _state!.Settings.Enabled ? "preparation_backoff" : "disabled";
                        Diagnostic("preparation_revoked", 0); return;
                    }
                    now = Now();
                    Commit(new LilithLettersCommand.ReconcileAttestedSources(history.UnattestedKeys, now));
                    before = _state!;
                }
                var material = history.Material;
                if (material.Count == 0) { lock (_sync) { _reason = "no_current_material"; Diagnostic(_reason, 0); } return; }
                var from = before.Batch?.FirstChatUtc ?? before.Turns.Where(t => material.Any(m => m.Key == t.Source.Key)).Min(t => t.Source.CompletedAtUtc);
                var collectionEnd = before.Batch?.SealedAtUtc ?? from + _timing.Collection(now);
                var end = now < collectionEnd ? now : collectionEnd;
                var displayLocale = locale switch { "zh-TW" => "zh-Hant-TW", "zh-CN" => "zh-Hans-CN", "ja" => "ja-JP", _ => "en" };
                phase = "packet_create";
                // Plan the exact reservation before building its packet, but do
                // not publish/charge it until the first model invocation. The
                // packet fingerprint then binds the actual committed revision.
                var reservationAt = now;
                var reserved = LilithLettersState.BeginEvaluation(before, reservationAt, _clock.TimeZone, _timing);
                var packet = LilithLettersWritingPacket.Create(reserved, material, [], displayLocale, from, end, now);
                if (packet is null) { lock (_sync) { _reason = "no_current_material"; Diagnostic("no_main_packet", 0); } return; }
                var expectedRevision = before.Revision;
                bool Fresh() { lock (_sync) return scope.IsCurrent && Current(canDraft) && _state!.Revision == expectedRevision; }
                // Adapter construction itself can fail without spending a model
                // attempt. No retry/refund is allowed once an invocation starts.
                var model = new ReservedTextModel(scope.Model(packet), callToken =>
                {
                    lock (_sync)
                    {
                        callToken.ThrowIfCancellationRequested();
                        if (!Fresh()) throw new OperationCanceledException("Letter preparation is no longer current.");
                        if (modelCalls == 0)
                        {
                            // The history/adapter preparation may span a clock
                            // jump or midnight. Never charge a stale day or use
                            // an untrusted clock to authorize a paid dispatch.
                            DateTimeOffset dispatchAt;
                            try { dispatchAt = Now(); }
                            catch (InvalidDataException) { throw new LilithLettersValidationException("preparation_clock_untrusted"); }
                            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(dispatchAt, _clock.TimeZone).DateTime)
                                != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reservationAt, _clock.TimeZone).DateTime))
                                throw new LilithLettersValidationException("preparation_day_changed");
                            Commit(new LilithLettersCommand.BeginEvaluation(reservationAt));
                            expectedRevision = _state!.Revision;
                            if (LilithLettersWritingPacket.Fingerprint(_state) != packet.StateFingerprint)
                                throw new InvalidDataException("Letter evaluation reservation changed.");
                            before = _state;
                            _reason = "writing";
                            Diagnostic("evaluation_started", 0);
                        }
                        modelCalls++;
                        phase = "writing";
                    }
                });
                var outcome = await new LilithLettersWriter(model, _time).RunAsync(packet, _ => Fresh(), token, classifyOnly: !canDraft).ConfigureAwait(false);
                lock (_sync) Diagnostic(outcome.DiagnosticCode ?? outcome.Status.ToString(), modelCalls);
                if (modelCalls == 0) { lock (_sync) _reason = _state!.Settings.Enabled ? "preparation_backoff" : "disabled"; return; }
                phase = "history_revalidate";
                var refreshedHistory = await scope.ReadAsync(before, token).ConfigureAwait(false);
                Work? queued = null;
                lock (_sync)
                {
                    if (refreshedHistory is null || !Fresh()) return;
                    var refreshed = refreshedHistory.Material;
                    now = Now();
                    // Missing/forgotten/edited sources are withdrawn, never repaired
                    // from cached model text. Keep durable tombstones for replay.
                    foreach (var item in material.Where(m => !refreshed.Any(r => r == m)))
                        Commit(new LilithLettersCommand.Withdraw(item.Key, now));
                    foreach (var meaning in outcome.Meanings)
                        Commit(new LilithLettersCommand.Classify(meaning.Key, meaning.Kind, now));
                    Commit(new LilithLettersCommand.Collect(now));
                    var draft = outcome.Draft;
                    if (outcome.Status != LilithLetterWritingStatus.Candidate || draft is null || now >= draft.ExpiresAtUtc
                        || draft.MainKeys.Concat(draft.BackdropKeys).Any(key => !refreshed.Any(r => r.Key == key)))
                    {
                        // Only a valid model deferral/no-material outcome says
                        // that no suitable content was found. Native failure,
                        // invalid shape or lost authority cannot say that.
                        _reason = outcome.Status is LilithLetterWritingStatus.Deferred or LilithLetterWritingStatus.NoMaterial
                            ? "no_suitable_letter" : "writing_unavailable";
                        return;
                    }
                    var gate = LilithLettersPolicy.Decide(_state!.Settings, _state.Batch, Opportunity(now), true,
                        new(_state.Deliveries.Select(d => d.Receipt)), _clock.TimeZone, _timing);
                    if (gate != LilithLetterGate.Ready) { _reason = gate.ToString(); return; }
                    var delivery = Guid.NewGuid();
                    var allowPresent = _timing.AllowsPresentTest(now) || (_state.Batch!.PendingAtRestart && !_timing.IsTest(now));
                    Commit(new LilithLettersCommand.Reserve(delivery, Opportunity(now), true));
                    Queue(delivery, LilithNoteInboxAction.Deliver, draft.Text, now);
                    _work!.AllowPresent = allowPresent;
                    queued = _work;
                    _reason = "awaiting_game_receipt";
                }
                // Retain the same session/model lease until the game completes
                // or the short delivery lease is revoked. Foreground turns and
                // settings changes cancel it through the host watcher.
                if (queued is not null)
                {
                    using var revoke = token.Register(() => { lock (_sync) Revoke(); });
                    try { await queued.Finished.Task.WaitAsync(TimeSpan.FromSeconds(31), token).ConfigureAwait(false); }
                    finally { lock (_sync) { if (ReferenceEquals(_work, queued)) Revoke(); } }
                }
            }, () => { lock (_sync) return Current(canDraft); }, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (_sync) { _reason = !_state!.Settings.Enabled ? "disabled" : modelCalls == 0 ? "preparation_backoff" : "writing_unavailable";
            Diagnostic(error is OperationCanceledException ? phase + "_cancelled"
            : error is LilithLettersValidationException validation ? validation.Code : phase + "_unavailable", modelCalls); Revoke(); } }
        finally
        {
            lock (_sync)
            {
                // A busy/revoked host can legitimately decline without entering
                // the callback. A finished worker must not look still preparing.
                if (_reason == "preparing")
                {
                    _reason = _state!.Settings.Enabled ? "preparation_backoff" : "disabled";
                    Diagnostic("preparation_not_started", modelCalls);
                }
            }
        }
    }

    private sealed class ReservedTextModel(ILilithLetterTextModel inner, Action<CancellationToken> reserve) : ILilithLetterTextModel
    {
        public Task<string> CompleteAsync(LilithLetterTextRequest request, CancellationToken cancellationToken)
        {
            reserve(cancellationToken);
            return inner.CompleteAsync(request, cancellationToken);
        }
    }

    private void Diagnostic(string code, int calls)
    {
        _lastDiagnostic = code;
        // One bounded last-result file, no raw prompt/reply, paths, secrets or exception text.
        try
        {
            var path = Path.Combine(_home, "letters-diagnostic.v1.json");
            var next = path + ".next";
            LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
            LilithRuntimeWorkspacePathGuard.EnsureSafe(next);
            File.WriteAllText(next, JsonSerializer.Serialize(new { observed_at_utc = _time.GetUtcNow(),
                code, model_calls = calls, test_timing = _timing.IsTest(_time.GetUtcNow()),
                test_expires_at_utc = _timing.TestExpiresAtUtc, revision = _state?.Revision,
                operator_test = _timing.AllowsPresentTest(_time.GetUtcNow()),
                player_absent = _opportunity?.PlayerAbsent, absent_since_utc = _absentSince }));
            File.Move(next, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or LilithRuntimeException) { }
    }

    private void Queue(Guid delivery, LilithNoteInboxAction action, string? text, DateTimeOffset now)
    {
        var scope = new LilithDailyNoteScope(LilithDailyNoteProtocol.ProfileId, delivery, Guid.NewGuid(), delivery,
            Guid.NewGuid(), _generation, _opportunity!.GameEpoch, Guid.NewGuid(), now.AddSeconds(30));
        _work = new(new(scope, action, text, _opportunity.Locale), _peer);
    }
    public async Task<LilithDailyNoteCommand?> PollAsync(string profileId, Guid gameEpoch, int waitMilliseconds,
        LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        if (waitMilliseconds is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        if (waitMilliseconds > 0) await Task.Delay(Math.Min(waitMilliseconds, 100), cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (!Current(false) || profileId != LilithDailyNoteProtocol.ProfileId || !SamePeer(_peer, peer)
                || _opportunity!.GameEpoch != gameEpoch || _work is not { PickedUp: false } work
                || work.Scope.ExpiresAtUtc <= _time.GetUtcNow()) return null;
            if (work.Command.Action == LilithNoteInboxAction.Deliver && !Current()) { Revoke(); return null; }
            if (work.Lease.IsCancellationRequested) return null;
            work.PickedUp = true;
            work.Lease.CancelAfter(_opportunity.ExpiresAtUtc - _time.GetUtcNow());
            return work.Command;
        }
    }
    public CancellationToken GetDeliveryLeaseCancellation(LilithDailyNoteScope scope, LilithGameNotesPeerIdentity peer)
    {
        lock (_sync) return _work is { PickedUp: true } w && w.Scope == scope && SamePeer(w.Peer, peer) && Current(false)
            ? w.Lease.Token : new(canceled: true);
    }
    public Task<bool> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_work is not { PickedUp: true } work || work.Scope != scope || !SamePeer(work.Peer, peer)) return Task.FromResult(false);
            LilithDailyNoteProtocol.RequireResult(scope, result);
            try
            {
                var phase = result.Status == LilithNoteInboxStatus.Delivered ? LilithLetterDeliveryPhase.Delivered
                    : work.Command.Action == LilithNoteInboxAction.Status && result.Status == LilithNoteInboxStatus.NotFound
                        ? LilithLetterDeliveryPhase.ConfirmedNoWrite : LilithLetterDeliveryPhase.Unknown;
                Commit(new LilithLettersCommand.Resolve(scope.DeliveryId, phase, Now(), result.Artifact?.CompletedAtUtc));
                _reason = phase == LilithLetterDeliveryPhase.Delivered ? "delivered" : "delivery_unresolved";
                work.Dispose(); _work = null; return Task.FromResult(true);
            }
            catch { _faulted = true; _reason = "receipt_unavailable"; return Task.FromResult(false); }
        }
    }
    private void Revoke()
    {
        if (_work is not { } work) return;
        work.Lease.Cancel();
        if (!work.PickedUp)
        {
            if (work.Command.Action == LilithNoteInboxAction.Deliver)
            {
                try { Commit(new LilithLettersCommand.Resolve(work.Scope.DeliveryId, LilithLetterDeliveryPhase.ConfirmedNoWrite, Now())); }
                catch { _faulted = true; _reason = "receipt_unavailable"; }
            }
            work.Dispose(); _work = null;
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task? worker;
        lock (_sync) { _disposed = true; _stop.Cancel(); Revoke(); worker = _worker; }
        if (worker is not null) { try { await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { } }
        lock (_sync) { _work?.Dispose(); _work = null; }
    }
    private sealed class Work(LilithDailyNoteCommand command, LilithGameNotesPeerIdentity peer) : IDisposable
    {
        internal LilithDailyNoteCommand Command = command;
        internal LilithDailyNoteScope Scope => Command.Scope;
        internal LilithGameNotesPeerIdentity Peer = peer;
        internal bool PickedUp;
        internal bool AllowPresent;
        internal TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenSource Lease = CancellationTokenSource.CreateLinkedTokenSource(peer.PeerExited);
        public void Dispose() { Lease.Cancel(); Finished.TrySetResult(); Lease.Dispose(); }
    }
}
