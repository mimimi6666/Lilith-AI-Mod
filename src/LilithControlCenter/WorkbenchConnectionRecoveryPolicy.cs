using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LilithControlCenter;

// Presentation recovery only: this budget never grants permission to replay a
// request, restart Runtime, discard a draft, or bypass an unfinished cleanup.
internal sealed class WorkbenchConnectionRecoveryPolicy
{
    internal const int MaximumAutomaticAttempts = 2;
    internal const int MaximumReadinessObservations = 30;
    internal const int MaximumOperationObservations = 6;
    internal static readonly TimeSpan ReadinessObservationInterval = TimeSpan.FromSeconds(1);
    private readonly HashSet<object> _automaticOwners = new(ReferenceEqualityComparer.Instance);

    internal bool TryReserveAutomaticAttempt(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return _automaticOwners.Count < MaximumAutomaticAttempts && _automaticOwners.Add(owner);
    }

    internal static async Task<bool> ObserveSameOperationAsync(
        Task operation,
        TimeSpan interval,
        Func<bool> stillCurrent,
        Action reportWaiting,
        Func<Task, TimeSpan, Task>? wait = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        wait ??= static (pending, timeout) => pending.WaitAsync(timeout);
        for (var observation = 0; observation < MaximumOperationObservations; ++observation)
        {
            if (!stillCurrent()) return false;
            try
            {
                await wait(operation, interval);
                return stillCurrent();
            }
            catch (TimeoutException)
            {
                // Completion racing the observation deadline still wins;
                // an operation's own timeout/failure is not a retry signal.
                if (operation.IsCompleted)
                {
                    await operation;
                    return stillCurrent();
                }
                if (!stillCurrent()) return false;
                reportWaiting();
            }
        }
        // The owner and operation remain alive. An explicit retry can join
        // them, but a stuck operation cannot generate an infinite retry loop.
        throw new TimeoutException("Connection recovery observation budget exhausted.");
    }
}
