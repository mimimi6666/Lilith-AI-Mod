using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LilithRuntimeContracts;

namespace LilithTextInjector;

// A transport pump only. It never owns a renderer, a path or a model call.
// One picked-up command waits for the Unity dispatcher, bounded by its lease.
internal sealed class LilithNativeNoteInboxToolBridge : IDisposable
{
    private readonly LilithGameHermesClient _client;
    private readonly Action<Exception>? _logFault;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<Pending> _queue = new();
    private readonly int _mainThread = Environment.CurrentManagedThreadId;
    private readonly Task _worker;
    private int _disposed;
    private int _faultLogged;
    private GenerationBinding? _generation;

    internal LilithNativeNoteInboxToolBridge(
        LilithGameHermesClient? client = null, Action<Exception>? logFault = null)
    {
        _client = client ?? new LilithGameHermesClient();
        _logFault = logFault;
        _worker = Task.Run(RunAsync);
    }

    internal bool MatchesRuntime(Guid generation)
        => Volatile.Read(ref _generation)?.Id == generation;

    internal void UpdateOnMainThread(Func<LilithNoteInboxCommand, LilithNoteInboxResult> execute)
    {
        if (Environment.CurrentManagedThreadId != _mainThread)
            throw new InvalidOperationException("NoteInbox requires the Unity thread.");
        if (!_queue.TryDequeue(out var pending)) return;
        if (_stop.IsCancellationRequested || pending.Cancelled
            || DateTimeOffset.UtcNow >= pending.Command.ExpiresAtUtc)
        {
            pending.Completion.TrySetResult(new LilithNoteInboxResult(
                pending.Command.OperationId, pending.Command.DeliveryId,
                LilithNoteInboxStatus.Rejected, "expired"));
            return;
        }
        try
        {
            pending.Completion.TrySetResult(execute(pending.Command));
        }
        catch (Exception exception)
        {
            LogFaultOnce(exception);
            // An executor fault may happen after writing. Never imply it is
            // safe to repeat the external operation.
            pending.Completion.TrySetResult(new LilithNoteInboxResult(
                pending.Command.OperationId, pending.Command.DeliveryId,
                LilithNoteInboxStatus.Unknown, "executor_fault"));
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var poll = await _client.PollNoteInboxAsync(
                        Guid.NewGuid(), 30_000, _stop.Token).ConfigureAwait(false);
                    if (!poll.Succeeded || poll.Command is null)
                    {
                        await Task.Delay(500, _stop.Token).ConfigureAwait(false);
                        continue;
                    }
                    var command = poll.Command;
                    if (command.RuntimeGeneration != poll.GenerationId)
                        throw new InvalidOperationException("NoteInbox generation mismatch.");
                    Volatile.Write(ref _generation, new GenerationBinding(poll.GenerationId));
                    var pending = new Pending(command);
                    LilithNoteInboxResult result;
                    var remaining = command.ExpiresAtUtc - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        result = new LilithNoteInboxResult(command.OperationId,
                            command.DeliveryId, LilithNoteInboxStatus.Rejected, "expired");
                    }
                    else
                    {
                        // Main-thread stalls must not accumulate retired leases.
                        while (_queue.TryPeek(out var retired) && retired.Cancelled)
                            _queue.TryDequeue(out _);
                        _queue.Enqueue(pending);
                        try
                        {
                            result = await pending.Completion.Task
                                .WaitAsync(remaining, _stop.Token).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            pending.Cancel();
                            result = new LilithNoteInboxResult(command.OperationId,
                                command.DeliveryId, LilithNoteInboxStatus.Unknown, "game_timeout");
                        }
                        finally
                        {
                            pending.Cancel();
                        }
                    }
                    // Completion transport may retry only the same receipt.
                    // No retry path calls the executor again.
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        try
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                            timeout.CancelAfter(TimeSpan.FromSeconds(5));
                            await _client.CompleteNoteInboxAsync(Guid.NewGuid(), result,
                                timeout.Token).ConfigureAwait(false);
                            break;
                        }
                        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                        { throw; }
                        catch (Exception exception)
                        { LogFaultOnce(exception); }
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                { return; }
                catch (Exception exception)
                {
                    Volatile.Write(ref _generation, null);
                    LogFaultOnce(exception);
                    await Task.Delay(2_000, _stop.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void LogFaultOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultLogged, 1) == 0)
        {
            try { _logFault?.Invoke(exception); }
            catch { /* Diagnostics never change delivery authority. */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        Volatile.Write(ref _generation, null);
        while (_queue.TryDequeue(out var pending)) pending.Cancel();
        _ = _worker.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record GenerationBinding(Guid Id);

    private sealed class Pending
    {
        private int _cancelled;
        internal Pending(LilithNoteInboxCommand command) => Command = command;
        internal LilithNoteInboxCommand Command { get; }
        internal bool Cancelled => Volatile.Read(ref _cancelled) != 0;
        internal void Cancel() => Interlocked.Exchange(ref _cancelled, 1);
        internal TaskCompletionSource<LilithNoteInboxResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
