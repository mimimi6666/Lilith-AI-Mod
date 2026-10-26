using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal interface ILilithGameNoteInboxHostBridge : IAsyncDisposable
{
    Task<LilithNoteInboxCommand?> PollAsync(int waitMilliseconds, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(LilithNoteInboxResult result, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken);
}

internal delegate bool LilithNoteInboxOwnerResolver(string sessionId, string originTurnId,
    out Guid clientTurnId, out CancellationToken turnCancellation);

// Independent of MemoBook: this bridge owns transport correlation, never PNG IO.
internal sealed class LilithGameNoteInboxHostBridge : ILilithGameNoteInboxHostBridge
{
    internal const int MaximumOperations = 4096;
    internal const int MaximumPending = 8;
    private readonly ILilithGameNotesGateway _gateway;
    private readonly Guid _generation;
    private readonly LilithNoteInboxOwnerResolver _resolveOwner;
    private readonly Func<string, CancellationToken, Task<bool>> _waitForOwner;
    private readonly TimeSpan _pickupTimeout;
    private readonly TimeSpan _completionTimeout;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Pending> _operations = new();
    private readonly Dictionary<Guid, Delivery> _deliveries = new();
    private readonly Queue<Pending> _available = new();
    private readonly HashSet<Task> _tasks = new();
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource<bool> _availableSignal = Signal();
    private bool _disposed;

    internal LilithGameNoteInboxHostBridge(LilithRuntimeService runtime, LilithRuntimeSnapshot snapshot,
        ILilithGameHermesSessionHost owner)
        : this(new LilithRuntimeGameNotesGateway(runtime), ReadyGeneration(snapshot),
            owner.TryResolveCurrentNoteInboxOwner, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9), owner.WaitForNoteInboxOwnerAsync)
    {
    }

    private static Guid ReadyGeneration(LilithRuntimeSnapshot snapshot)
        => snapshot.State == LilithRuntimeState.Ready && snapshot.GatewayReady
            ? snapshot.GenerationId : throw new ArgumentException("A ready note Runtime is required.");

    internal LilithGameNoteInboxHostBridge(ILilithGameNotesGateway gateway, Guid generation,
        LilithNoteInboxOwnerResolver resolveOwner, TimeSpan pickupTimeout, TimeSpan completionTimeout,
        Func<string, CancellationToken, Task<bool>>? waitForOwner = null)
    {
        if (generation == Guid.Empty) throw new ArgumentException("Runtime generation required.");
        if (pickupTimeout <= TimeSpan.Zero || completionTimeout <= TimeSpan.Zero
            || pickupTimeout + completionTimeout > TimeSpan.FromSeconds(12))
            throw new ArgumentException("Note delivery lifetime must be bounded by twelve seconds.");
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _generation = generation; _resolveOwner = resolveOwner ?? throw new ArgumentNullException(nameof(resolveOwner));
        _waitForOwner = waitForOwner ?? ((_, _) => Task.FromResult(false));
        _pickupTimeout = pickupTimeout; _completionTimeout = completionTimeout;
        _gateway.GatewayEvent += OnEvent;
    }

    private void OnEvent(HermesGatewayEvent value)
    {
        if (_disposed || value.Type != "lilith.host.request" || value.Payload.ValueKind != JsonValueKind.Object
            || !value.Payload.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
            || kind.GetString() != LilithNoteInboxProtocol.HostRequestKind) return;
        var task = HandleAsync(value);
        lock (_sync) _tasks.Add(task);
        _ = task.ContinueWith(done =>
        {
            _ = done.Exception;
            lock (_sync) _tasks.Remove(done);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task HandleAsync(HermesGatewayEvent value)
    {
        string session; string turn; Guid operation; Guid delivery; JsonElement arguments;
        try
        {
            session = value.SessionId;
            turn = LilithNoteInboxProtocol.String(value.Parameters, "turn_id");
            LilithNoteInboxProtocol.Exact(value.Payload, "version", "kind", "operation_id", "delivery_id", "arguments");
            if (value.Payload.GetProperty("version").GetInt32() != 1) return;
            operation = LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(value.Payload, "operation_id"));
            delivery = LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(value.Payload, "delivery_id"));
            if (delivery != LilithNoteInboxProtocol.DeriveDeliveryId(session, turn)) return;
            arguments = value.Payload.GetProperty("arguments");
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or FormatException)
        { return; }

        var admittedAt = DateTimeOffset.UtcNow;
        var ownerResolved = _resolveOwner(session, turn, out var client, out var turnCancellation);
        if (!ownerResolved)
        {
            using var readiness = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            readiness.CancelAfter(_pickupTimeout < TimeSpan.FromMilliseconds(250) ? _pickupTimeout : TimeSpan.FromMilliseconds(250));
            try { await _waitForOwner(session, readiness.Token).WaitAsync(readiness.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            ownerResolved = _resolveOwner(session, turn, out client, out turnCancellation);
        }
        if (!ownerResolved || turnCancellation.IsCancellationRequested)
        {
            await AckAsync(session, turn, new(operation, delivery, LilithNoteInboxStatus.Deferred,
                "runtime.note_owner_unavailable"), _lifetime.Token).ConfigureAwait(false);
            return;
        }
        LilithNoteInboxCommand command;
        try
        {
            command = LilithNoteInboxProtocol.DecodeArguments(arguments, operation, delivery, turn, client,
                admittedAt + _pickupTimeout + _completionTimeout, _generation);
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            await AckAsync(session, turn, new(operation, delivery, LilithNoteInboxStatus.Rejected,
                "runtime.note_arguments_rejected"), _lifetime.Token).ConfigureAwait(false);
            return;
        }
        Pending pending;
        LilithNoteInboxResult? immediate = null;
        lock (_sync)
        {
            if (_disposed) return;
            pending = new(session, command, turnCancellation);
            if (_operations.TryGetValue(operation, out var existing))
            {
                if (existing.Session == session && SameCommand(existing.Metadata, command))
                {
                    if (existing.Result is null) return;
                    pending = existing;
                    immediate = existing.Result;
                }
                else immediate = new(operation, delivery, LilithNoteInboxStatus.Rejected, "runtime.note_operation_conflict");
            }
            else if (_operations.Count >= MaximumOperations || _operations.Values.Count(p => p.Result is null) >= MaximumPending)
                immediate = new(operation, delivery, LilithNoteInboxStatus.Deferred, "runtime.note_capacity");
            if (immediate is null && command.Action == LilithNoteInboxAction.Deliver)
            {
                var digest = PayloadHash(command);
                if (_deliveries.TryGetValue(delivery, out var previous))
                {
                    immediate = previous.Hash != digest
                        ? new(operation, delivery, LilithNoteInboxStatus.Rejected, "runtime.note_payload_conflict")
                        : previous.Result is { } prior
                            ? new(operation, delivery, prior.Status, prior.Code, prior.Artifact)
                            : new(operation, delivery, LilithNoteInboxStatus.Deferred, "runtime.note_delivery_pending");
                }
                else _deliveries.Add(delivery, new(digest));
            }
            if (!_operations.ContainsKey(operation) && _operations.Count < MaximumOperations)
                _operations.Add(operation, pending);
            if (immediate is null)
            {
                _available.Enqueue(pending);
                var signal = _availableSignal; _availableSignal = Signal(); signal.TrySetResult(true);
            }
            else { pending.Result = immediate; pending.Command = null; }
        }
        if (immediate is null)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, turnCancellation);
            try
            {
                var pickupRemaining = admittedAt + _pickupTimeout - DateTimeOffset.UtcNow;
                if (pickupRemaining <= TimeSpan.Zero) throw new TimeoutException();
                await pending.PickedUp.Task.WaitAsync(pickupRemaining, linked.Token).ConfigureAwait(false);
                var remaining = command.ExpiresAtUtc - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                await pending.Finished.Task.WaitAsync(remaining, linked.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                lock (_sync)
                    FinishLocked(pending, new(operation, delivery,
                        pending.Peer.HasValue ? LilithNoteInboxStatus.Unknown : LilithNoteInboxStatus.Deferred,
                        pending.Peer.HasValue ? "runtime.note_outcome_unknown" : "runtime.note_not_picked_up"));
            }
        }
        LilithNoteInboxResult result;
        lock (_sync) result = pending.Result!;
        try
        {
            var acknowledged = await AckAsync(session, turn, result, _lifetime.Token).ConfigureAwait(false);
            pending.Acknowledged.TrySetResult(acknowledged);
        }
        catch { pending.Acknowledged.TrySetResult(false); }
    }

    public async Task<LilithNoteInboxCommand?> PollAsync(int waitMilliseconds, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        if (waitMilliseconds is < 0 or > 30_000) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        if (!peer.IsValid || _disposed) return null;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(waitMilliseconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, peer.PeerExited, _lifetime.Token);
        do
        {
            Task wake;
            lock (_sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                while (_available.TryDequeue(out var pending))
                {
                    if (pending.Result is not null || pending.Peer.HasValue) continue;
                    if (!CurrentOwner(pending) || pending.Metadata.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                    {
                        FinishLocked(pending, new(pending.Metadata.OperationId, pending.Metadata.DeliveryId,
                            LilithNoteInboxStatus.Deferred, "runtime.note_owner_retired"));
                        continue;
                    }
                    pending.Peer = peer;
                    pending.PickedUp.TrySetResult(true);
                    pending.PeerExitRegistration = peer.PeerExited.Register(() =>
                    {
                        lock (_sync) FinishLocked(pending, new(pending.Metadata.OperationId, pending.Metadata.DeliveryId,
                            LilithNoteInboxStatus.Unknown, "runtime.note_peer_exited"));
                    });
                    return pending.Command;
                }
                wake = _availableSignal.Task;
            }
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (waitMilliseconds == 0 || remaining <= TimeSpan.Zero) return null;
            try { await wake.WaitAsync(remaining, linked.Token).ConfigureAwait(false); }
            catch (TimeoutException) { return null; }
        } while (!linked.IsCancellationRequested);
        return null;
    }

    public async Task<bool> CompleteAsync(LilithNoteInboxResult result, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        Pending pending;
        lock (_sync)
        {
            if (_disposed || !peer.IsValid || !_operations.TryGetValue(result.OperationId, out pending!)
                || pending.Metadata.DeliveryId != result.DeliveryId || pending.Peer is not { } owner
                || owner.ProcessId != peer.ProcessId || owner.ProcessCreationTime != peer.ProcessCreationTime) return false;
            if (pending.Result is { } prior)
            {
                if (!SameResult(prior, result)) return false;
            }
            else
            {
                if (!CurrentOwner(pending) || DateTimeOffset.UtcNow >= pending.Metadata.ExpiresAtUtc) return false;
                if (result.Artifact is { } artifact && artifact.CompletedAtUtc > DateTimeOffset.UtcNow) return false;
                FinishLocked(pending, result);
            }
        }
        return await pending.Acknowledged.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool CurrentOwner(Pending pending) => !pending.TurnCancellation.IsCancellationRequested
        && _resolveOwner(pending.Session, pending.Metadata.OriginTurnId, out var client, out var cancellation)
        && client == pending.Metadata.ClientTurnId && !cancellation.IsCancellationRequested;

    private void FinishLocked(Pending pending, LilithNoteInboxResult result)
    {
        if (pending.Result is not null) return;
        pending.Result = result;
        pending.Command = null;
        pending.PeerExitRegistration.Unregister();
        // Never evict or replay an unresolved external effect within this generation.
        if (pending.Metadata.Action == LilithNoteInboxAction.Deliver && _deliveries.TryGetValue(result.DeliveryId, out var delivery))
            delivery.Result = result;
        pending.PickedUp.TrySetResult(false);
        pending.Finished.TrySetResult(true);
    }

    private async Task<bool> AckAsync(string session, string turn, LilithNoteInboxResult result, CancellationToken cancellationToken)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(4));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var one = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            one.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var ack = await _gateway.RequestAsync("lilith.host.complete", new
                {
                    session_id = session, turn_id = turn, result = LilithNoteInboxProtocol.EncodeResult(result)
                }, one.Token).WaitAsync(one.Token).ConfigureAwait(false);
                LilithNoteInboxProtocol.Exact(ack, "accepted", "operation_id", "delivery_id");
                if (ack.GetProperty("accepted").ValueKind == JsonValueKind.True
                    && LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(ack, "operation_id")) == result.OperationId
                    && LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(ack, "delivery_id")) == result.DeliveryId) return true;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { }
            if (total.IsCancellationRequested) break;
        }
        return false;
    }

    private static string PayloadHash(LilithNoteInboxCommand command)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { command.OriginTurnId, command.Text, command.Locale }))));
    private static bool SameCommand(CommandMetadata left, LilithNoteInboxCommand right)
        => left.OperationId == right.OperationId && left.DeliveryId == right.DeliveryId
            && left.OriginTurnId == right.OriginTurnId && left.ClientTurnId == right.ClientTurnId
            && left.Action == right.Action && left.PayloadHash == PayloadHash(right);
    private static bool SameResult(LilithNoteInboxResult left, LilithNoteInboxResult right)
        => LilithNoteInboxProtocol.EncodeResult(left).GetRawText() == LilithNoteInboxProtocol.EncodeResult(right).GetRawText();
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _gateway.GatewayEvent -= OnEvent; _lifetime.Cancel();
        Task[] tasks;
        lock (_sync)
        {
            foreach (var pending in _operations.Values)
            {
                FinishLocked(pending, new(pending.Metadata.OperationId, pending.Metadata.DeliveryId,
                    pending.Peer.HasValue ? LilithNoteInboxStatus.Unknown : LilithNoteInboxStatus.Deferred,
                    "runtime.note_shutdown"));
                pending.Acknowledged.TrySetResult(false);
            }
            tasks = _tasks.ToArray(); _availableSignal.TrySetResult(false);
        }
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
        _lifetime.Dispose();
    }

    private sealed class Pending
    {
        internal readonly string Session;
        internal LilithNoteInboxCommand? Command;
        internal readonly CommandMetadata Metadata;
        internal readonly CancellationToken TurnCancellation;
        internal readonly TaskCompletionSource<bool> PickedUp = Signal();
        internal readonly TaskCompletionSource<bool> Finished = Signal();
        internal readonly TaskCompletionSource<bool> Acknowledged = Signal();
        internal LilithGameNotesPeerIdentity? Peer;
        internal CancellationTokenRegistration PeerExitRegistration;
        internal LilithNoteInboxResult? Result;
        internal Pending(string session, LilithNoteInboxCommand command, CancellationToken cancellation)
        {
            Session = session; Command = command; TurnCancellation = cancellation;
            Metadata = new(command.OperationId, command.DeliveryId, command.OriginTurnId,
                command.ClientTurnId, command.Action, command.ExpiresAtUtc, PayloadHash(command));
        }
    }
    private sealed record CommandMetadata(Guid OperationId, Guid DeliveryId, string OriginTurnId,
        Guid ClientTurnId, LilithNoteInboxAction Action, DateTimeOffset ExpiresAtUtc, string PayloadHash);
    private sealed class Delivery
    {
        internal readonly string Hash;
        internal LilithNoteInboxResult? Result;
        internal Delivery(string hash) => Hash = hash;
    }
}

internal sealed class LilithUnavailableGameNoteInboxHostBridge : ILilithGameNoteInboxHostBridge
{
    internal static readonly LilithUnavailableGameNoteInboxHostBridge Instance = new();
    public Task<LilithNoteInboxCommand?> PollAsync(int waitMilliseconds, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
        => Task.FromResult<LilithNoteInboxCommand?>(null);
    public Task<bool> CompleteAsync(LilithNoteInboxResult result, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
        => Task.FromResult(false);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
