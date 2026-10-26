using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal readonly record struct LilithGameNotesPeerIdentity(
    uint ProcessId,
    ulong ProcessCreationTime,
    CancellationToken PeerExited)
{
    internal bool IsValid
        => ProcessId != 0
            && ProcessCreationTime != 0
            && !PeerExited.IsCancellationRequested;
}

internal interface ILilithGameNotesHostBridge : IAsyncDisposable
{
    Task<LilithMemoCommand?> PollAsync(
        int waitMilliseconds,
        LilithGameNotesPeerIdentity peer,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        LilithMemoResult result,
        LilithGameNotesPeerIdentity peer,
        CancellationToken cancellationToken);
}

internal interface ILilithGameNotesGateway
{
    event Action<HermesGatewayEvent>? GatewayEvent;

    Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken);
}

internal sealed class LilithRuntimeGameNotesGateway : ILilithGameNotesGateway
{
    private readonly LilithRuntimeService _runtime;

    internal LilithRuntimeGameNotesGateway(LilithRuntimeService runtime)
        => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public event Action<HermesGatewayEvent>? GatewayEvent
    {
        add => _runtime.GatewayEvent += value;
        remove => _runtime.GatewayEvent -= value;
    }

    public Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
        => _runtime.RequestAsync(method, parameters, cancellationToken);
}

internal sealed class LilithUnavailableGameNotesHostBridge : ILilithGameNotesHostBridge
{
    internal static LilithUnavailableGameNotesHostBridge Instance { get; } = new();

    public Task<LilithMemoCommand?> PollAsync(
        int waitMilliseconds,
        LilithGameNotesPeerIdentity peer,
        CancellationToken cancellationToken)
        => Task.FromResult<LilithMemoCommand?>(null);

    public Task<bool> CompleteAsync(
        LilithMemoResult result,
        LilithGameNotesPeerIdentity peer,
        CancellationToken cancellationToken)
        => Task.FromResult(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class LilithGameNotesHostBridge : ILilithGameNotesHostBridge
{
    internal const string HostRequestEventType = "lilith.host.request";
    internal const string HostRequestKind = "lilith.notes.host-request";
    internal const string HostCompleteMethod = "lilith.host.complete";
    internal const int HostRequestVersion = 1;
    internal const int MaximumPendingOperations = 32;

    private const int MaximumCompletionReceipts = 64;
    private const int MaximumHermesSessionIdUtf8Bytes = 240;
    private const int MaximumHostCompletionAttempts = 2;
    private static readonly TimeSpan HostCompletionTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan HostCompletionAttemptTimeout =
        TimeSpan.FromSeconds(2);

    private readonly ILilithGameNotesGateway _gateway;
    private readonly TimeSpan _gamePickupTimeout;
    private readonly TimeSpan _gameCompletionTimeout;
    private readonly object _sync = new();
    private readonly Queue<PendingOperation> _available = new();
    private readonly Dictionary<Guid, PendingOperation> _pending = new();
    private readonly Dictionary<Guid, CompletionReceipt> _completionReceipts = new();
    private readonly Queue<Guid> _completionReceiptOrder = new();
    private readonly HashSet<Task> _backgroundTasks = new();
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource<bool> _availableSignal = NewSignal();
    private bool _disposed;

    internal LilithGameNotesHostBridge(
        LilithRuntimeService runtime,
        LilithRuntimeSnapshot snapshot)
        : this(
            new LilithRuntimeGameNotesGateway(runtime),
            ValidateSnapshot(snapshot),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(9))
    {
    }

    internal LilithGameNotesHostBridge(
        ILilithGameNotesGateway gateway,
        Guid generationId,
        TimeSpan gamePickupTimeout,
        TimeSpan gameCompletionTimeout)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        if (generationId == Guid.Empty)
            throw new ArgumentException("A Runtime generation identity is required.", nameof(generationId));
        if (gamePickupTimeout <= TimeSpan.Zero || gamePickupTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(gamePickupTimeout));
        if (gameCompletionTimeout <= TimeSpan.Zero || gameCompletionTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(gameCompletionTimeout));
        _gamePickupTimeout = gamePickupTimeout;
        _gameCompletionTimeout = gameCompletionTimeout;
        gateway.GatewayEvent += RuntimeGatewayEvent;
    }

    public async Task<LilithMemoCommand?> PollAsync(
        int waitMilliseconds,
        LilithGameNotesPeerIdentity peer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (waitMilliseconds is < 0 or > LilithGameBridgeContracts.LilithGameBridgeProtocol.MaximumMemoWaitMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        if (!peer.IsValid)
            return null;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            peer.PeerExited,
            _lifetime.Token);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(waitMilliseconds);
        while (!linked.IsCancellationRequested)
        {
            Task signal;
            lock (_sync)
            {
                while (_available.TryDequeue(out var pending))
                {
                    if (pending.Terminal || pending.Owner.HasValue)
                        continue;
                    pending.Owner = peer;
                    pending.Delivered.TrySetResult(true);
                    ObservePeerExit(pending, peer);
                    return pending.Command;
                }
                if (waitMilliseconds == 0)
                    return null;
                signal = _availableSignal.Task;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return null;
            try
            {
                await signal.WaitAsync(remaining, linked.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
        return null;
    }

    public async Task<bool> CompleteAsync(
        LilithMemoResult result,
        LilithGameNotesPeerIdentity peer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_disposed || !peer.IsValid)
            return false;
        PendingOperation pending;
        lock (_sync)
        {
            if (_completionReceipts.TryGetValue(result.OperationId, out var receipt))
            {
                return receipt.ProcessId == peer.ProcessId
                    && receipt.ProcessCreationTime == peer.ProcessCreationTime
                    && ResultsEqual(receipt.Result, result);
            }
            if (!_pending.TryGetValue(result.OperationId, out var found)
                || found is null
                || found.Owner is not { } owner
                || owner.ProcessId != peer.ProcessId
                || owner.ProcessCreationTime != peer.ProcessCreationTime
                || !ResultMatchesCommand(found.Command, result))
            {
                return false;
            }
            pending = found;
            if (pending.Outcome is not null)
            {
                if (!ResultsEqual(pending.Outcome, result))
                    return false;
            }
            else if (!TryTransitionLocked(pending, result))
            {
                return false;
            }
        }

        try
        {
            return await pending.HostAcknowledged.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private void RuntimeGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        if (_disposed
            || !string.Equals(gatewayEvent.Type, HostRequestEventType, StringComparison.Ordinal))
        {
            return;
        }
        var task = HandleHostRequestAsync(gatewayEvent);
        lock (_sync)
            _backgroundTasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (_sync)
                    _backgroundTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleHostRequestAsync(HermesGatewayEvent gatewayEvent)
    {
        HostRequest request;
        try
        {
            request = ParseHostRequest(gatewayEvent);
        }
        catch
        {
            return;
        }

        LilithMemoResult result;
        PendingOperation? pending = null;
        lock (_sync)
        {
            if (_completionReceipts.TryGetValue(request.Command.OperationId, out var cached))
            {
                result = string.Equals(
                        cached.SessionId,
                        request.SessionId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        cached.TurnId,
                        request.TurnId,
                        StringComparison.Ordinal)
                        ? cached.Result
                        : Failure(
                            request.Command.OperationId,
                            "runtime.memo_correlation_conflict");
            }
            else if (_pending.ContainsKey(request.Command.OperationId))
            {
                return;
            }
            else if (_pending.Count >= MaximumPendingOperations)
            {
                result = Failure(request.Command.OperationId, "runtime.memo_queue_full");
            }
            else
            {
                pending = new PendingOperation(request);
                _pending.Add(request.Command.OperationId, pending);
                _available.Enqueue(pending);
                var signal = _availableSignal;
                _availableSignal = NewSignal();
                signal.TrySetResult(true);
                result = null!;
            }
        }

        if (pending is not null)
        {
            try
            {
                await pending.Delivered.Task
                    .WaitAsync(_gamePickupTimeout, _lifetime.Token)
                    .ConfigureAwait(false);
                result = await pending.Completion.Task
                    .WaitAsync(_gameCompletionTimeout, _lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                lock (_sync)
                {
                    if (pending.Outcome is null)
                    {
                        _ = TryTransitionLocked(
                            pending,
                            pending.Owner.HasValue
                                ? Failure(
                                    request.Command.OperationId,
                                    "runtime.memo_outcome_unknown")
                                : Failure(
                                    request.Command.OperationId,
                                    "runtime.game_unavailable"));
                    }
                    result = pending.Outcome!;
                }
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    if (pending.Outcome is null)
                    {
                        _ = TryTransitionLocked(
                            pending,
                            Failure(
                                request.Command.OperationId,
                                "runtime.memo_unavailable"));
                    }
                    result = pending.Outcome!;
                }
            }
        }

        try
        {
            await CompleteHostRequestAsync(request, result, _lifetime.Token)
                .ConfigureAwait(false);
            lock (_sync)
            {
                if (!_completionReceipts.ContainsKey(result.OperationId))
                {
                    if (pending is null)
                        AddCompletionReceipt(request, result);
                    else
                        AddCompletionReceipt(pending, result);
                }
                pending?.HostAcknowledged.TrySetResult(true);
            }
        }
        catch
        {
            lock (_sync)
                pending?.HostAcknowledged.TrySetResult(false);
            throw;
        }
        finally
        {
            if (pending is not null)
            {
                lock (_sync)
                {
                    if (_pending.TryGetValue(
                            request.Command.OperationId,
                            out var current)
                        && ReferenceEquals(current, pending))
                    {
                        _pending.Remove(request.Command.OperationId);
                    }
                }
                pending.Dispose();
            }
        }
    }

    private async Task CompleteHostRequestAsync(
        HostRequest request,
        LilithMemoResult result,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(LilithMemoProtocol.EncodeResult(result));
        var resultElement = document.RootElement.Clone();
        using var completionLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        completionLifetime.CancelAfter(HostCompletionTimeout);
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MaximumHostCompletionAttempts; attempt++)
        {
            try
            {
                using var attemptLifetime = CancellationTokenSource
                    .CreateLinkedTokenSource(completionLifetime.Token);
                attemptLifetime.CancelAfter(HostCompletionAttemptTimeout);
                var completion = await _gateway.RequestAsync(
                    HostCompleteMethod,
                    new
                    {
                        session_id = request.SessionId,
                        turn_id = request.TurnId,
                        result = resultElement
                    },
                    attemptLifetime.Token).ConfigureAwait(false);
                RequireExact(completion, "accepted", "operation_id");
                if (!completion.TryGetProperty("accepted", out var accepted)
                    || accepted.ValueKind is not JsonValueKind.True
                    || !completion.TryGetProperty("operation_id", out var operation)
                    || operation.ValueKind != JsonValueKind.String
                    || !IsHermesCorrelationIdentity(operation.GetString())
                    || !Guid.TryParseExact(
                        operation.GetString(),
                        "N",
                        out var operationId)
                    || operationId != result.OperationId)
                {
                    throw new InvalidDataException(
                        "Hermes memo host completion receipt was rejected.");
                }
                return;
            }
            catch (Exception exception) when (
                attempt < MaximumHostCompletionAttempts
                && !completionLifetime.IsCancellationRequested)
            {
                lastFailure = exception;
            }
        }
        throw lastFailure
            ?? new TimeoutException("Hermes memo host completion timed out.");
    }

    private static HostRequest ParseHostRequest(HermesGatewayEvent gatewayEvent)
    {
        if (!IsHermesSessionIdentity(gatewayEvent.SessionId)
            || gatewayEvent.Parameters.ValueKind != JsonValueKind.Object
            || !gatewayEvent.Parameters.TryGetProperty("turn_id", out var turnValue)
            || turnValue.ValueKind != JsonValueKind.String
            || !IsHermesCorrelationIdentity(turnValue.GetString())
            || gatewayEvent.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Hermes memo host correlation was rejected.");
        }

        RequireExact(
            gatewayEvent.Payload,
            "version", "kind", "operation_id", "arguments");
        if (!gatewayEvent.Payload.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var parsedVersion)
            || parsedVersion != HostRequestVersion
            || !gatewayEvent.Payload.TryGetProperty("kind", out var kind)
            || kind.ValueKind != JsonValueKind.String
            || !string.Equals(kind.GetString(), HostRequestKind, StringComparison.Ordinal)
            || !gatewayEvent.Payload.TryGetProperty("operation_id", out var operation)
            || operation.ValueKind != JsonValueKind.String
            || !IsHermesCorrelationIdentity(operation.GetString())
            || !Guid.TryParseExact(operation.GetString(), "N", out var operationId)
            || operationId == Guid.Empty
            || !gatewayEvent.Payload.TryGetProperty("arguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Hermes memo host request was rejected.");
        }
        return new HostRequest(
            gatewayEvent.SessionId,
            turnValue.GetString()!,
            LilithMemoProtocol.DecodeArguments(arguments, operationId));
    }

    private void ObservePeerExit(
        PendingOperation pending,
        LilithGameNotesPeerIdentity peer)
    {
        if (!peer.PeerExited.CanBeCanceled)
            return;
        var task = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                peer.PeerExited,
                pending.OwnerWatchCancellation.Token,
                _lifetime.Token);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (_lifetime.IsCancellationRequested
                    || pending.OwnerWatchCancellation.IsCancellationRequested)
                    return;
                lock (_sync)
                {
                    _ = TryTransitionLocked(
                        pending,
                        Failure(
                            pending.Command.OperationId,
                            "runtime.memo_outcome_unknown"));
                }
            }
        });
        lock (_sync)
            _backgroundTasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (_sync)
                    _backgroundTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void AddCompletionReceipt(
        PendingOperation pending,
        LilithMemoResult result)
    {
        var peer = pending.Owner;
        _completionReceipts[result.OperationId] = new CompletionReceipt(
            pending.SessionId,
            pending.TurnId,
            result,
            peer?.ProcessId ?? 0,
            peer?.ProcessCreationTime ?? 0);
        _completionReceiptOrder.Enqueue(result.OperationId);
        TrimCompletionReceipts();
    }

    private void AddCompletionReceipt(HostRequest request, LilithMemoResult result)
    {
        _completionReceipts[result.OperationId] = new CompletionReceipt(
            request.SessionId,
            request.TurnId,
            result,
            0,
            0);
        _completionReceiptOrder.Enqueue(result.OperationId);
        TrimCompletionReceipts();
    }

    private static bool TryTransitionLocked(
        PendingOperation pending,
        LilithMemoResult outcome)
    {
        if (pending.Outcome is not null)
            return false;
        pending.Outcome = outcome;
        pending.OwnerWatchCancellation.Cancel();
        pending.Completion.TrySetResult(outcome);
        return true;
    }

    private static bool ResultsEqual(LilithMemoResult left, LilithMemoResult right)
        => LilithMemoProtocol.EncodeResult(left)
            .AsSpan()
            .SequenceEqual(LilithMemoProtocol.EncodeResult(right));

    private static bool ResultMatchesCommand(
        LilithMemoCommand command,
        LilithMemoResult result)
    {
        if (result.OperationId != command.OperationId
            || result.Succeeded != string.Equals(
                result.Code,
                "ok",
                StringComparison.Ordinal))
        {
            return false;
        }
        if (!result.Succeeded)
            return result.Items.Count <= 1;

        return command.Action switch
        {
            LilithMemoAction.List => result.Items.Count <= command.Limit,
            LilithMemoAction.Delete => result.Items.Count == 0,
            LilithMemoAction.CreateScratch => result.Items.Count == 1
                && result.Items[0].Kind == LilithMemoKind.Scratch,
            LilithMemoAction.CreateTodo => result.Items.Count == 1
                && result.Items[0].Kind == LilithMemoKind.Todo,
            LilithMemoAction.Get or LilithMemoAction.Update =>
                IsExactTarget(result, command.MemoId),
            LilithMemoAction.Promote => IsExactTarget(result, command.MemoId)
                && result.Items[0].Kind == LilithMemoKind.Todo,
            LilithMemoAction.Complete => IsExactTarget(result, command.MemoId)
                && result.Items[0].Kind == LilithMemoKind.Todo,
            _ => false
        };
    }

    private static bool IsExactTarget(LilithMemoResult result, int memoId)
        => result.Items.Count == 1 && result.Items[0].MemoId == memoId;

    private void TrimCompletionReceipts()
    {
        while (_completionReceiptOrder.Count > MaximumCompletionReceipts)
            _completionReceipts.Remove(_completionReceiptOrder.Dequeue());
    }

    private static LilithMemoResult Failure(Guid operationId, string code)
        => new(operationId, succeeded: false, code);

    private static bool IsHermesSessionIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.IndexOf('\0') < 0
            && Encoding.UTF8.GetByteCount(value) <= MaximumHermesSessionIdUtf8Bytes;

    private static bool IsHermesCorrelationIdentity(string? value)
        => value is { Length: 32 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RequireExact(JsonElement value, params string[] names)
    {
        var expected = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("Hermes memo host property was rejected.");
        }
        if (!seen.SetEquals(expected))
            throw new InvalidDataException("Hermes memo host property was missing.");
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Guid ValidateSnapshot(LilithRuntimeSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.GenerationId == Guid.Empty
            || snapshot.State != LilithRuntimeState.Ready
            || !snapshot.GatewayReady)
        {
            throw new ArgumentException("A ready Runtime snapshot is required.", nameof(snapshot));
        }
        return snapshot.GenerationId;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gateway.GatewayEvent -= RuntimeGatewayEvent;
        _lifetime.Cancel();
        Task[] background;
        lock (_sync)
        {
            foreach (var pending in _pending.Values)
            {
                pending.OwnerWatchCancellation.Cancel();
                pending.Delivered.TrySetCanceled(_lifetime.Token);
                pending.Completion.TrySetCanceled(_lifetime.Token);
                pending.HostAcknowledged.TrySetResult(false);
            }
            _pending.Clear();
            background = _backgroundTasks.ToArray();
        }
        try
        {
            await Task.WhenAll(background).WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch
        {
        }
        _lifetime.Dispose();
    }

    private sealed record HostRequest(
        string SessionId,
        string TurnId,
        LilithMemoCommand Command);

    private sealed class PendingOperation : IDisposable
    {
        internal string SessionId { get; }
        internal string TurnId { get; }
        internal LilithMemoCommand Command { get; }
        internal TaskCompletionSource<bool> Delivered { get; } = NewSignal();
        internal TaskCompletionSource<LilithMemoResult> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> HostAcknowledged { get; } = NewSignal();
        internal CancellationTokenSource OwnerWatchCancellation { get; } = new();
        internal LilithGameNotesPeerIdentity? Owner { get; set; }
        internal LilithMemoResult? Outcome { get; set; }
        internal bool Terminal => Outcome is not null;

        internal PendingOperation(HostRequest request)
        {
            SessionId = request.SessionId;
            TurnId = request.TurnId;
            Command = request.Command;
        }

        public void Dispose()
        {
            OwnerWatchCancellation.Cancel();
            OwnerWatchCancellation.Dispose();
        }
    }

    private sealed record CompletionReceipt(
        string SessionId,
        string TurnId,
        LilithMemoResult Result,
        uint ProcessId,
        ulong ProcessCreationTime);
}
