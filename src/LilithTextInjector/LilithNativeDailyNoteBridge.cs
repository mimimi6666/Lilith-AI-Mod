using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;

namespace LilithTextInjector;

internal interface ILilithDailyNoteGameTransport
{
    Task<LilithGameBridgeDailyNoteOpportunityResponse> OfferAsync(LilithDailyNoteOpportunity opportunity, CancellationToken cancellation);
    Task<LilithDailyNotePolledCommand> PollAsync(string profileId, Guid gameEpoch, CancellationToken cancellation);
    Task<LilithGameBridgeDailyNoteCompleteResponse> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, CancellationToken cancellation);
}

// Publishes main-thread facts; never evaluates Unity state, renders, or calls a model.
internal sealed class LilithNativeDailyNoteBridge : IDisposable
{
    private readonly ILilithDailyNoteGameTransport _transport;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<Exception>? _fault;
    private readonly int _mainThread = Environment.CurrentManagedThreadId;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<Pending> _queue = new();
    private readonly Task _worker;
    private LilithDailyNoteOpportunity? _opportunity;
    private Generation? _generation;
    private Guid _gameEpoch = Guid.NewGuid();
    private static long _processSequence;
    private bool _wasSafe;
    private bool _wasEnabled;
    private int _disposed;
    private int _revocationPublishing;
    private Pending? _executing;

    internal LilithNativeDailyNoteBridge(Action<Exception>? fault = null)
        : this(new Transport(new LilithGameHermesClient()), null, fault) { }

    internal LilithNativeDailyNoteBridge(ILilithDailyNoteGameTransport transport,
        Func<DateTimeOffset>? utcNow = null, Action<Exception>? fault = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow); _fault = fault;
        _worker = Task.Run(RunAsync);
    }

    internal void PublishOnMainThread(bool enabled, bool safe, string locale, bool playerAbsent = false)
    {
        RequireMainThread();
        if (_disposed != 0) return;
        if (_wasSafe && !safe || _wasEnabled && !enabled) RotateEpoch();
        _wasSafe = enabled && safe; _wasEnabled = enabled;
        var now = _utcNow();
        Volatile.Write(ref _opportunity, new LilithDailyNoteOpportunity(LilithDailyNoteProtocol.ProfileId,
            _gameEpoch, Interlocked.Increment(ref _processSequence), enabled, enabled && safe, locale, now, now.AddSeconds(5), playerAbsent));
    }

    internal void RevokeOnMainThread(bool disable = false)
    {
        RequireMainThread();
        RotateEpoch();
        var previous = Volatile.Read(ref _opportunity);
        _wasSafe = false;
        if (disable) _wasEnabled = false;
        var now = _utcNow();
        Volatile.Write(ref _opportunity, new LilithDailyNoteOpportunity(LilithDailyNoteProtocol.ProfileId,
            _gameEpoch, Interlocked.Increment(ref _processSequence), _wasEnabled, false, previous?.Locale ?? "zh-TW", now, now.AddSeconds(5)));
        // F8/Off revocation must not wait behind a pending completion exchange.
        if (Interlocked.CompareExchange(ref _revocationPublishing, 1, 0) == 0)
            _ = PublishRevocationAsync(Volatile.Read(ref _opportunity)!);
    }

    private async Task PublishRevocationAsync(LilithDailyNoteOpportunity opportunity)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _transport.OfferAsync(opportunity, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) { Report(exception); }
        finally { Interlocked.Exchange(ref _revocationPublishing, 0); }
    }

    internal bool Matches(LilithDailyNoteScope scope, bool requireSafe)
    {
        var opportunity = Volatile.Read(ref _opportunity);
        var now = _utcNow();
        return _disposed == 0 && !_stop.IsCancellationRequested && opportunity is not null
            && opportunity.Enabled && (!requireSafe || opportunity.Safe)
            && now >= opportunity.ObservedAtUtc && now < opportunity.ExpiresAtUtc
            && scope.ProfileId == opportunity.ProfileId && scope.GameEpoch == opportunity.GameEpoch
            && scope.RuntimeGeneration == Volatile.Read(ref _generation)?.Id
            && now < scope.ExpiresAtUtc && scope.ExpiresAtUtc - now <= TimeSpan.FromSeconds(60)
            && (Volatile.Read(ref _executing) is not { } executing
                || executing.Command.Scope == scope && !executing.Cancelled
                    && now < executing.PickupDeadlineUtc && executing.Lease.IsAlive);
    }

    internal int PendingCount => _queue.Count;

    internal void UpdateOnMainThread(Func<LilithDailyNoteCommand, LilithNoteInboxResult> execute)
    {
        RequireMainThread();
        if (!_queue.TryDequeue(out var pending)) return;
        if (pending.Cancelled || !pending.Lease.IsAlive || _utcNow() >= pending.PickupDeadlineUtc
            || !Matches(pending.Command.Scope, requireSafe: pending.Command.Action == LilithNoteInboxAction.Deliver))
        {
            pending.Result.TrySetResult(Result(pending.Command, LilithNoteInboxStatus.Rejected, "daily.lease_revoked"));
            return;
        }
        Volatile.Write(ref _executing, pending);
        try { pending.Result.TrySetResult(execute(pending.Command)); }
        catch (Exception exception)
        {
            Report(exception);
            pending.Result.TrySetResult(Result(pending.Command, LilithNoteInboxStatus.Unknown, "daily.executor_fault"));
        }
        finally { Volatile.Write(ref _executing, null); }
    }

    private async Task RunAsync()
    {
        long lastPublished = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var opportunity = Volatile.Read(ref _opportunity);
                    if (opportunity is null || _utcNow() >= opportunity.ExpiresAtUtc)
                    { await Task.Delay(100, _stop.Token).ConfigureAwait(false); continue; }
                    if (opportunity.Sequence != lastPublished)
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(3));
                        var offered = await _transport.OfferAsync(opportunity, deadline.Token).ConfigureAwait(false);
                        Volatile.Write(ref _generation, new Generation(offered.GenerationId));
                        lastPublished = opportunity.Sequence;
                    }
                    if (!opportunity.Enabled) { await Task.Delay(100, _stop.Token).ConfigureAwait(false); continue; }
                    var polled = await _transport.PollAsync(opportunity.ProfileId, opportunity.GameEpoch, _stop.Token).ConfigureAwait(false);
                    using var executionLease = polled.Lease;
                    var poll = polled.Response;
                    Volatile.Write(ref _generation, new Generation(poll.GenerationId));
                    if (!poll.Succeeded || poll.Command is null) { await Task.Delay(100, _stop.Token).ConfigureAwait(false); continue; }
                    var command = poll.Command;
                    LilithNoteInboxResult result;
                    if (executionLease is null || !executionLease.IsAlive || !Matches(command.Scope, command.Action == LilithNoteInboxAction.Deliver))
                        result = Result(command, LilithNoteInboxStatus.Rejected, "daily.lease_revoked");
                    else
                    {
                        while (_queue.TryPeek(out var retired) && retired.Cancelled) _queue.TryDequeue(out _);
                        var pickup = Volatile.Read(ref _opportunity)!;
                        var pickupDeadline = pickup.ExpiresAtUtc < command.Scope.ExpiresAtUtc ? pickup.ExpiresAtUtc : command.Scope.ExpiresAtUtc;
                        var pending = new Pending(command, pickupDeadline, executionLease); _queue.Enqueue(pending);
                        try
                        {
                            var remaining = pending.PickupDeadlineUtc - _utcNow();
                            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                            result = await pending.Result.Task.WaitAsync(remaining, _stop.Token).ConfigureAwait(false);
                        }
                        catch (TimeoutException) { result = Result(command, LilithNoteInboxStatus.Unknown, "daily.game_timeout"); }
                        finally { pending.Cancel(); }
                    }
                    // Retry only an identical completion. No retry can execute a PNG write.
                    for (var retry = 0; retry < 2; retry++)
                    {
                        try
                        {
                            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                            deadline.CancelAfter(TimeSpan.FromSeconds(3));
                            await _transport.CompleteAsync(command.Scope, result, deadline.Token).ConfigureAwait(false);
                            break;
                        }
                        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
                        catch (Exception exception) { Report(exception); }
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    Volatile.Write(ref _generation, null); Report(exception);
                    await Task.Delay(1000, _stop.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void RotateEpoch()
    {
        _gameEpoch = Guid.NewGuid();
        while (_queue.TryDequeue(out var pending))
        {
            pending.Cancel();
            pending.Result.TrySetResult(Result(pending.Command, LilithNoteInboxStatus.Rejected, "daily.lease_revoked"));
        }
    }

    private void RequireMainThread()
    { if (Environment.CurrentManagedThreadId != _mainThread) throw new InvalidOperationException("Daily note state requires the Unity thread."); }

    private void Report(Exception exception)
    { try { _fault?.Invoke(exception); } catch { } }

    private static LilithNoteInboxResult Result(LilithDailyNoteCommand command, LilithNoteInboxStatus status, string code)
        => new(command.Scope.OperationId, command.Scope.DeliveryId, status, code);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); Volatile.Write(ref _generation, null);
        while (_queue.TryDequeue(out var pending)) { pending.Cancel(); pending.Result.TrySetCanceled(); }
        _ = _worker.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record Generation(Guid Id);
    private sealed class Pending
    {
        private int _cancelled;
        internal Pending(LilithDailyNoteCommand command, DateTimeOffset pickupDeadlineUtc, ILilithDailyNoteExecutionLease lease)
        { Command = command; PickupDeadlineUtc = pickupDeadlineUtc; Lease = lease; }
        internal LilithDailyNoteCommand Command { get; }
        internal DateTimeOffset PickupDeadlineUtc { get; }
        internal ILilithDailyNoteExecutionLease Lease { get; }
        internal bool Cancelled => Volatile.Read(ref _cancelled) != 0;
        internal void Cancel() => Interlocked.Exchange(ref _cancelled, 1);
        internal TaskCompletionSource<LilithNoteInboxResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Transport : ILilithDailyNoteGameTransport
    {
        private readonly LilithGameHermesClient _client;
        internal Transport(LilithGameHermesClient client) => _client = client;
        public Task<LilithGameBridgeDailyNoteOpportunityResponse> OfferAsync(LilithDailyNoteOpportunity opportunity, CancellationToken cancellation)
            => _client.PublishDailyNoteOpportunityAsync(Guid.NewGuid(), opportunity, cancellation);
        public Task<LilithDailyNotePolledCommand> PollAsync(string profileId, Guid gameEpoch, CancellationToken cancellation)
            => _client.PollDailyNoteLeaseAsync(Guid.NewGuid(), profileId, gameEpoch, cancellation);
        public Task<LilithGameBridgeDailyNoteCompleteResponse> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, CancellationToken cancellation)
            => _client.CompleteDailyNoteAsync(Guid.NewGuid(), scope, result, cancellation);
    }
}
