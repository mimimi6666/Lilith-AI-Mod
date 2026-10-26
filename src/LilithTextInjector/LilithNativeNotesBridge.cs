using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LilithRuntimeContracts;

namespace LilithTextInjector;

/// <summary>
/// Owns the game-notes transport loop. Network work stays on a worker thread;
/// official game objects are touched only while UpdateOnMainThread is called by
/// DialogueManager.Update's postfix.
/// </summary>
internal static class LilithNativeNotesBridge
{
    private const int PollWaitMilliseconds = 30_000;
    private const int RememberedTerminalCapacity = 64;
    private const int MaximumCompletionTransportAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CompletionRetryDelay =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CompletionAttemptTimeout =
        // Runtime may make two bounded, idempotent host-ack attempts inside
        // a four-second envelope. Keep this transport read alive slightly
        // longer so a healthy second acknowledgement is not cut off at the
        // game pipe one second early.
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EmptyPollDelay = TimeSpan.FromMilliseconds(250);
    private static readonly LilithGameHermesClient Client = new();
    private static readonly ConcurrentQueue<PendingMemoCommand> PendingMainThread =
        new();
    private static readonly CancellationTokenSource Shutdown = new();
    private static int _workerStarted;
    private static int _faultLogged;
    private static int _mainThreadId;

    internal static void UpdateOnMainThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        var knownMainThreadId = Volatile.Read(ref _mainThreadId);
        if (knownMainThreadId == 0)
        {
            Interlocked.CompareExchange(
                ref _mainThreadId,
                currentThreadId,
                comparand: 0);
            knownMainThreadId = Volatile.Read(ref _mainThreadId);
        }
        if (knownMainThreadId != currentThreadId)
            throw new InvalidOperationException(
                "Native notes may be pumped only by the Unity main thread.");

        EnsureWorkerStarted();
        if (!PendingMainThread.TryDequeue(out var pending))
            return;

        LilithMemoResult result;
        try
        {
            result = LilithNativeMemoBookAdapter.ExecuteOnMainThread(
                pending.Command);
        }
        catch (Exception exception)
        {
            LogFaultOnce(exception);
            result = new LilithMemoResult(
                pending.Command.OperationId,
                succeeded: false,
                "game_executor_fault");
        }
        pending.Completion.TrySetResult(result);
    }

    private static void EnsureWorkerStarted()
    {
        if (Interlocked.Exchange(ref _workerStarted, 1) != 0)
            return;
        _ = Task.Run(() => RunAsync(Shutdown.Token));
    }

    private static async Task RunAsync(CancellationToken cancellationToken)
    {
        var remembered = new Dictionary<Guid, LilithMemoResult>();
        var rememberedOrder = new Queue<Guid>();
        while (!cancellationToken.IsCancellationRequested)
        {
            LilithGameBridgeContracts.LilithGameBridgeMemoPollResponse poll;
            try
            {
                poll = await Client.PollMemosAsync(
                        Guid.NewGuid(),
                        PollWaitMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _faultLogged, 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFaultOnce(exception);
                await DelayForRetryAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!poll.Succeeded)
            {
                await DelayForRetryAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (poll.Command is null)
            {
                await Task.Delay(EmptyPollDelay, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var command = poll.Command;
            if (!remembered.TryGetValue(command.OperationId, out var result))
            {
                var pending = new PendingMemoCommand(command);
                PendingMainThread.Enqueue(pending);
                try
                {
                    result = await pending.Completion.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                RememberTerminal(
                    result,
                    remembered,
                    rememberedOrder);
            }

            // A completion write may have reached Runtime even if its response
            // is lost. Retry the exact terminal result and never re-run the
            // official mutation for this operation identity.
            for (var attempt = 1;
                 attempt <= MaximumCompletionTransportAttempts
                    && !cancellationToken.IsCancellationRequested;
                 attempt++)
            {
                try
                {
                    using var attemptTimeout = CancellationTokenSource
                        .CreateLinkedTokenSource(cancellationToken);
                    attemptTimeout.CancelAfter(CompletionAttemptTimeout);
                    var completion = await Client.CompleteMemoAsync(
                            Guid.NewGuid(),
                            result,
                            attemptTimeout.Token)
                        .ConfigureAwait(false);
                    // Explicit rejection is definitive for this delivery
                    // attempt. Return to poll; if Runtime offers the operation
                    // again, the remembered terminal is delivered without
                    // re-running MemoBook.
                    break;
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    LogFaultOnce(exception);
                }
                if (attempt < MaximumCompletionTransportAttempts)
                {
                    await Task.Delay(CompletionRetryDelay, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private static void RememberTerminal(
        LilithMemoResult result,
        IDictionary<Guid, LilithMemoResult> remembered,
        Queue<Guid> rememberedOrder)
    {
        remembered[result.OperationId] = result;
        rememberedOrder.Enqueue(result.OperationId);
        while (rememberedOrder.Count > RememberedTerminalCapacity)
            remembered.Remove(rememberedOrder.Dequeue());
    }

    private static async Task DelayForRetryAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void LogFaultOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultLogged, 1) != 0)
            return;
        Plugin.PluginLog?.LogWarning(
            "Official notes bridge is waiting for recovery ("
            + exception.GetType().Name
            + ").");
    }

    private sealed class PendingMemoCommand
    {
        internal PendingMemoCommand(LilithMemoCommand command)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Completion = new TaskCompletionSource<LilithMemoResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal LilithMemoCommand Command { get; }
        internal TaskCompletionSource<LilithMemoResult> Completion { get; }
    }
}
