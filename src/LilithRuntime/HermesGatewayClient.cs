using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace LilithRuntime;

internal sealed class HermesGatewayClient : IAsyncDisposable
{
    private const int MaximumMessageBytes = 32 * 1024 * 1024;
    private const string RuntimeTrustHeader = "X-Lilith-Runtime-Trust";

    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<
        long,
        TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _eventLock = new();
    private readonly Dictionary<(string Type, string SessionId), HermesGatewayEvent>
        _latestEvents = new();
    private readonly List<EventWaiter> _eventWaiters = new();
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _receiveLoop;
    private long _nextRequestId;
    private bool _stopping;

    internal event Action<HermesGatewayEvent>? EventReceived;

    internal async Task ConnectAsync(
        Uri uri,
        string runtimeTrustToken,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeTrustToken);
        _socket.Options.SetRequestHeader(RuntimeTrustHeader, runtimeTrustToken);
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _socket.ConnectAsync(uri, timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LilithRuntimeException(
                "Hermes WebSocket connection timed out.");
        }
        catch (Exception)
        {
            throw new LilithRuntimeException(
                "Hermes WebSocket connection failed.");
        }

        _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
        try
        {
            await _ready.Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new LilithRuntimeException(
                "Hermes did not publish gateway.ready in time.");
        }
    }

    internal async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("A JSON-RPC method is required.", nameof(method));
        }

        if (_socket.State != WebSocketState.Open)
        {
            throw new HermesRequestNotSentException(
                method,
                new LilithRuntimeException(
                    "The Hermes gateway is not connected."));
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new HermesRequestNotSentException(
                method,
                new LilithRuntimeException(
                    "A JSON-RPC request identity collision occurred."));
        }

        try
        {
            ArraySegment<byte> payload;
            try
            {
                payload = SerializeRequest(id, method, parameters);
                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new HermesRequestNotSentException(method, exception);
            }
            try
            {
                // From this invocation boundary onward the WebSocket may have
                // committed any prefix of the frame.  Failures must never be
                // represented as safely retryable.
                await _socket.SendAsync(
                    payload,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            return await completion.Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new LilithRuntimeException(
                $"Hermes JSON-RPC method '{method}' timed out.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    internal async Task<HermesGatewayEvent> WaitForEventAsync(
        Func<HermesGatewayEvent, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        EventWaiter waiter;
        lock (_eventLock)
        {
            var cached = _latestEvents.Values.FirstOrDefault(predicate);
            if (cached is not null)
            {
                return cached;
            }

            waiter = new EventWaiter(predicate);
            _eventWaiters.Add(waiter);
        }

        try
        {
            return await waiter.Completion.Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new LilithRuntimeException(
                "The expected Hermes gateway event did not arrive in time.");
        }
        finally
        {
            lock (_eventLock)
            {
                _eventWaiters.Remove(waiter);
            }
        }
    }

    internal async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Lilith Runtime stopping",
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _lifetime.Cancel();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        _socket.Dispose();
        _lifetime.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested
                && _socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(
                        rented,
                        cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new LilithRuntimeException(
                            "Hermes sent a non-text JSON-RPC frame.");
                    }

                    if (message.Length + result.Count > MaximumMessageBytes)
                    {
                        throw new LilithRuntimeException(
                            "Hermes sent an oversized JSON-RPC frame.");
                    }

                    message.Write(rented, 0, result.Count);
                }
                while (!result.EndOfMessage);

                ProcessFrame(message.GetBuffer().AsMemory(0, checked((int)message.Length)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_stopping)
            {
                FailAll(new LilithRuntimeException(
                    "The Hermes gateway receive loop stopped unexpectedly."));
                _ready.TrySetException(
                    exception is LilithRuntimeException
                        ? exception
                        : new LilithRuntimeException(
                            "The Hermes gateway receive loop failed."));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private void ProcessFrame(ReadOnlyMemory<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8);
        var root = document.RootElement;
        if (root.TryGetProperty("method", out var method)
            && string.Equals(method.GetString(), "event", StringComparison.Ordinal))
        {
            ProcessEvent(root);
            return;
        }

        if (!root.TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt64(out var id)
            || !_pending.TryGetValue(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var codeElement)
                && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : -1;
            var message = error.TryGetProperty("message", out var messageElement)
                ? SanitizeRpcMessage(messageElement.GetString())
                : "Hermes JSON-RPC request failed.";
            completion.TrySetException(new HermesRpcException(code, message));
            return;
        }

        if (root.TryGetProperty("result", out var result))
        {
            completion.TrySetResult(result.Clone());
        }
        else
        {
            completion.TrySetException(new HermesRpcException(
                -1,
                "Hermes returned a JSON-RPC response without a result."));
        }
    }

    private void ProcessEvent(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString() ?? string.Empty;
        var sessionId = parameters.TryGetProperty("session_id", out var session)
            ? session.GetString() ?? string.Empty
            : string.Empty;
        var payload = parameters.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : default;
        var gatewayEvent = new HermesGatewayEvent(
            type,
            sessionId,
            payload,
            parameters.Clone());

        List<EventWaiter> matches;
        lock (_eventLock)
        {
            if (_latestEvents.Count >= 256
                && !_latestEvents.ContainsKey((type, sessionId)))
            {
                _latestEvents.Remove(_latestEvents.Keys.First());
            }

            _latestEvents[(type, sessionId)] = gatewayEvent;
            matches = _eventWaiters
                .Where(waiter => waiter.Matches(gatewayEvent))
                .ToList();
            foreach (var waiter in matches)
            {
                _eventWaiters.Remove(waiter);
            }
        }

        foreach (var waiter in matches)
        {
            waiter.Completion.TrySetResult(gatewayEvent);
        }

        if (string.Equals(type, "gateway.ready", StringComparison.Ordinal))
        {
            _ready.TrySetResult(true);
        }

        var handlers = EventReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<HermesGatewayEvent> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(gatewayEvent);
            }
            catch
            {
            }
        }
    }

    private void FailAll(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }

        lock (_eventLock)
        {
            foreach (var waiter in _eventWaiters)
            {
                waiter.Completion.TrySetException(exception);
            }

            _eventWaiters.Clear();
        }
    }

    private static ArraySegment<byte> SerializeRequest(
        long id,
        string method,
        object? parameters)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters is null)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                JsonSerializer.Serialize(writer, parameters, parameters.GetType());
            }

            writer.WriteEndObject();
        }

        return new ArraySegment<byte>(buffer.WrittenSpan.ToArray());
    }

    private static string SanitizeRpcMessage(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? "Hermes JSON-RPC request failed."
            : value.Trim();
        text = new string(text
            .Where(character => !char.IsControl(character) || character == ' ')
            .Take(512)
            .ToArray());
        return text.Length == 0
            ? "Hermes JSON-RPC request failed."
            : text;
    }

    private sealed class EventWaiter
    {
        private readonly Func<HermesGatewayEvent, bool> _predicate;

        internal TaskCompletionSource<HermesGatewayEvent> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal EventWaiter(Func<HermesGatewayEvent, bool> predicate)
        {
            _predicate = predicate;
        }

        internal bool Matches(HermesGatewayEvent gatewayEvent)
        {
            try
            {
                return _predicate(gatewayEvent);
            }
            catch
            {
                return false;
            }
        }
    }
}

internal sealed class HermesRequestNotSentException : Exception
{
    internal HermesRequestNotSentException(
        string method,
        Exception innerException)
        : base(
            $"Hermes JSON-RPC method '{method}' was not sent.",
            innerException)
    {
    }
}
