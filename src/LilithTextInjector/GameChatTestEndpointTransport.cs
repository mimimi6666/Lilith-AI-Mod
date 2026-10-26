using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal sealed class GameChatTestEndpoint : IDisposable
{
    private readonly GameChatTestEndpointOptions _options;
    private readonly GameChatTestEndpointBroker _broker;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private volatile bool _faulted;
    private int _disposed;
    internal Guid RunId => _options.RunId;
    internal string PipeName => _options.PipeName;
    internal DateTimeOffset ExpiresAtUtc => _options.ExpiresAtUtc;
    internal bool IsActive => !_faulted && Volatile.Read(ref _disposed) == 0 && _broker.IsActive;

    private GameChatTestEndpoint(GameChatTestEndpointOptions options)
    {
        _options = options; _broker = new GameChatTestEndpointBroker(options);
        _lifetime.CancelAfter(options.Lifetime);
        _worker = Task.Run(RunAsync);
    }

    internal static GameChatTestEndpoint? TryStartFromProcessEnvironment()
    {
        GameChatTestEndpointOptions? options;
        try
        {
            options = GameChatTestEndpointOptions.TryRead(Environment.GetEnvironmentVariable,
                DateTimeOffset.UtcNow, Environment.ProcessId);
        }
        finally
        {
            // No secret command-line argument, persistent setting or child-process inheritance.
            Environment.SetEnvironmentVariable(GameChatTestEndpointOptions.EnabledVariable, null);
            Environment.SetEnvironmentVariable(GameChatTestEndpointOptions.TokenVariable, null);
            Environment.SetEnvironmentVariable(GameChatTestEndpointOptions.ExpiryVariable, null);
        }
        if (options == null) return null;
        if (!OperatingSystem.IsWindows()) { options.Dispose(); return null; }
        try { return new GameChatTestEndpoint(options); }
        catch { options.Dispose(); return null; }
    }

    internal bool TryDequeue(out GameChatTestEndpointCommand? command) => _broker.TryDequeue(out command);
    internal bool Complete(GameChatTestEndpointCommand command, GameChatTestEndpointReceipt receipt)
        => _broker.Complete(command, receipt);

    private async Task RunAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested && _broker.IsActive)
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    GameChatTestEndpointCodec.MaximumFrameBytes, GameChatTestEndpointCodec.MaximumFrameBytes);
                await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                // One request per connection. Partial/slow frames cannot retain the only slot.
                using var exchange = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                exchange.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var request = GameChatTestEndpointCodec.Decode(
                        await GameChatTestEndpointCodec.ReadFrameAsync(pipe, exchange.Token).ConfigureAwait(false));
                    using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(exchange.Token);
                    dispatch.CancelAfter(TimeSpan.FromMilliseconds(GameChatTestEndpointBroker.DispatchDeadlineMilliseconds));
                    var receipt = await _broker.Enqueue(request, dispatch.Token).ConfigureAwait(false);
                    await GameChatTestEndpointCodec.WriteFrameAsync(pipe,
                        GameChatTestEndpointCodec.Encode(RunId, request.RequestId, request.TargetId, receipt),
                        exchange.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or JsonException
                    or OperationCanceledException or ArgumentException or InvalidOperationException)
                {
                    // Close malformed/disconnected requests. Never log tokens, messages or raw errors.
                    // A lost acknowledgement is not permission to submit the message a second time.
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch { _faulted = true; }
        finally { _broker.Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); _broker.Dispose();
        _ = _worker.ContinueWith(_ => _lifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
