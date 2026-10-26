using System;

namespace LilithTextInjector;

internal enum D045AgentTaskPhase
{
    Planning = 0,
    Executing = 1,
    GoalVerified = 2,
    FinalizingWithoutTools = 3,
    Completed = 4,
    Stopped = 5,
    Cancelled = 6
}

internal enum D045AgentTaskDirective
{
    Ignore = 0,
    ContinueExecution = 1,
    FinalizeWithoutTools = 2,
    PublishProviderReply = 3,
    PublishVerifiedFallback = 4,
    PublishUnverifiedStop = 5
}

/// <summary>
/// Owns the terminal lifecycle for one explicit local-agent turn. Tool codecs
/// remain provider-specific, while completion precedence and exactly-once
/// publication remain provider-neutral and host-owned.
/// </summary>
internal sealed class D045VerifiedGoalTerminalKernel
{
    private readonly object _gate = new();
    private D045AgentTaskPhase _phase = D045AgentTaskPhase.Planning;
    private D045AgentTaskStopReason _stopReason =
        D045AgentTaskStopReason.None;

    internal D045AgentTaskPhase Phase
    {
        get
        {
            lock (_gate)
                return _phase;
        }
    }

    internal bool EffectsMayRun
    {
        get
        {
            lock (_gate)
            {
                return _phase is D045AgentTaskPhase.Planning
                    or D045AgentTaskPhase.Executing;
            }
        }
    }

    internal D045AgentTaskStopReason StopReason
    {
        get
        {
            lock (_gate)
                return _stopReason;
        }
    }

    internal bool IsFinalizingWithoutTools
    {
        get
        {
            lock (_gate)
            {
                return _phase
                    == D045AgentTaskPhase.FinalizingWithoutTools;
            }
        }
    }

    internal D045AgentTaskDirective ObserveToolBatch(
        bool goalVerified,
        bool executionMustStop)
        => ObserveToolBatch(
            goalVerified,
            executionMustStop,
            executionMustStop
                ? D045AgentTaskStopReason.GoalIncomplete
                : D045AgentTaskStopReason.None);

    internal D045AgentTaskDirective ObserveToolBatch(
        bool goalVerified,
        bool executionMustStop,
        D045AgentTaskStopReason stopReason)
    {
        lock (_gate)
        {
            if (_phase is D045AgentTaskPhase.Completed
                or D045AgentTaskPhase.Stopped
                or D045AgentTaskPhase.Cancelled
                or D045AgentTaskPhase.FinalizingWithoutTools)
            {
                return D045AgentTaskDirective.Ignore;
            }

            if (_phase == D045AgentTaskPhase.Planning)
                _phase = D045AgentTaskPhase.Executing;

            // A typed, goal-matching verified receipt always wins over a
            // generic no-progress, round or deadline stop discovered in the
            // same batch. No later effect may run after this transition.
            if (goalVerified)
            {
                _phase = D045AgentTaskPhase.GoalVerified;
                _phase = D045AgentTaskPhase.FinalizingWithoutTools;
                return D045AgentTaskDirective.FinalizeWithoutTools;
            }

            if (executionMustStop)
            {
                DemandStopReason(stopReason);
                _stopReason = stopReason;
                _phase = IsCancellation(stopReason)
                    ? D045AgentTaskPhase.Cancelled
                    : D045AgentTaskPhase.Stopped;
                return D045AgentTaskDirective.PublishUnverifiedStop;
            }

            return D045AgentTaskDirective.ContinueExecution;
        }
    }

    internal D045AgentTaskDirective ObserveStop(
        D045AgentTaskStopReason stopReason)
    {
        DemandStopReason(stopReason);
        lock (_gate)
        {
            if (_phase is D045AgentTaskPhase.Completed
                or D045AgentTaskPhase.Stopped
                or D045AgentTaskPhase.Cancelled
                or D045AgentTaskPhase.FinalizingWithoutTools)
            {
                return D045AgentTaskDirective.Ignore;
            }

            _stopReason = stopReason;
            _phase = IsCancellation(stopReason)
                ? D045AgentTaskPhase.Cancelled
                : D045AgentTaskPhase.Stopped;
            return D045AgentTaskDirective.PublishUnverifiedStop;
        }
    }

    internal D045AgentTaskDirective ObserveFinalization(
        bool providerReplyIsUsable)
    {
        lock (_gate)
        {
            if (_phase != D045AgentTaskPhase.FinalizingWithoutTools)
                return D045AgentTaskDirective.Ignore;

            _phase = D045AgentTaskPhase.Completed;
            return providerReplyIsUsable
                ? D045AgentTaskDirective.PublishProviderReply
                : D045AgentTaskDirective.PublishVerifiedFallback;
        }
    }

    private static bool IsCancellation(D045AgentTaskStopReason stopReason)
        => stopReason is D045AgentTaskStopReason.OperationCancelled
            or D045AgentTaskStopReason.RequiredPermissionRevoked;

    private static void DemandStopReason(
        D045AgentTaskStopReason stopReason)
    {
        if (stopReason == D045AgentTaskStopReason.None
            || !Enum.IsDefined(typeof(D045AgentTaskStopReason), stopReason))
        {
            throw new ArgumentOutOfRangeException(nameof(stopReason));
        }
    }
}
