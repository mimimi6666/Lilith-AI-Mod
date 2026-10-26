using System;

namespace LilithTextInjector;

internal static class D029CharacterProjectionPrerequisite
{
    internal static bool IsReady(
        CharacterProjectionObserveStatus status,
        bool hasAcceptedProjection)
        => status == CharacterProjectionObserveStatus.ProjectionOnly
            && hasAcceptedProjection;
}

internal static class D029ProactiveCoexistencePrerequisite
{
    internal static bool IsReady(bool officialNoteWasJustDelivered)
        => !officialNoteWasJustDelivered;
}

internal static class D029ProactiveCapacityPrerequisite
{
    internal static bool IsReady(
        bool coexistenceReady,
        bool aiCooldownActive,
        bool weeklyCapacityAvailable)
        => coexistenceReady
            && !aiCooldownActive
            && weeklyCapacityAvailable;
}

internal static class D029PendingNoteSchedulerGate
{
    internal static bool ShouldHold(
        D029SourceHostStatus sourceStatus,
        D029IntegrationStep? currentStep)
        => sourceStatus is D029SourceHostStatus.ReadyToArm
                or D029SourceHostStatus.Armed
                or D029SourceHostStatus.Running
            && currentStep is not D029IntegrationStep.ProactiveInteraction
            && currentStep is not D029IntegrationStep.CombinedStress;
}

/// <summary>
/// Shapes only the isolated D-029 MotionAssist probe into the single
/// allowlisted PlayOneShot semantic kind after Motion Director has already
/// admitted the current situation as safe to dispatch. This consumes no
/// additional RNG and leaves every ordinary Motion Director proposal
/// byte-for-byte equivalent outside the dedicated probe step. Queue, Policy,
/// Dispatcher and Execution Authority remain independently authoritative.
/// </summary>
internal static class D029MotionAssistOpportunitySelector
{
    internal static MotionDirectorProposal Select(
        D029IntegrationStep? step,
        MotionDirectorProposal proposal)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (step != D029IntegrationStep.MotionAssist
            || proposal.Disposition != MotionDirectorDisposition.Dispatch
            || proposal.Kind == MotionIntentKind.PlayOneShot)
        {
            return proposal;
        }

        return MotionDirectorProposal.Propose(
            MotionIntentKind.PlayOneShot,
            MotionDirectorReason.React,
            proposal.ProviderId,
            observeOnly: false);
    }
}

/// <summary>
/// Selects exactly one already-authoritative Motion Director proposal and its
/// correlated completion for the bounded CombinedStress observation. Ambient
/// Motion Director activity continues unchanged; later proposals are outside
/// the D-029 sample instead of being misclassified as duplicate execution.
/// </summary>
internal sealed class D029CombinedStressMotionObservationGate
{
    private Guid? _proposalId;
    private bool _completionRecorded;

    internal bool TryCaptureProposal(
        D029IntegrationStep? step,
        Guid proposalId)
    {
        if (step != D029IntegrationStep.CombinedStress)
        {
            Reset();
            return false;
        }
        if (proposalId == Guid.Empty || _proposalId.HasValue)
            return false;

        _proposalId = proposalId;
        return true;
    }

    internal bool TryCaptureCompletion(
        D029IntegrationStep? step,
        Guid proposalId,
        bool completed)
    {
        if (step != D029IntegrationStep.CombinedStress)
        {
            Reset();
            return false;
        }
        if (!completed
            || !_proposalId.HasValue
            || _completionRecorded
            || _proposalId.Value != proposalId)
        {
            return false;
        }

        _completionRecorded = true;
        return true;
    }

    internal void Reset()
    {
        _proposalId = null;
        _completionRecorded = false;
    }
}

internal enum D029PluginSignal
{
    Invalid = 0,
    PromptAndCharacterProjected = 1,
    MemoryPacketAdmitted = 2,
    ProviderCallStarted = 3,
    PlayerReplyCompleted = 4,
    ProactiveNoteCompleted = 5,
    ProactiveCooldownBlocked = 6,
    D022QueueSubmitted = 7,
    D022MotionCompleted = 8,
    MotionDirectorQueueSubmitted = 9,
    MotionDirectorMotionCompleted = 10
}

internal enum D029PluginSignalAdmission
{
    Invalid = 0,
    Recorded = 1,
    StepCompleted = 2,
    RunCompleted = 3,
    Disabled = 4,
    NotArmed = 5,
    Terminal = 6,
    RuntimeMismatch = 7,
    Expired = 8,
    CounterMismatch = 9
}

internal sealed class D029PluginRuntimeSnapshot
{
    internal D029SourceHostSnapshot Source { get; }
    internal D029IntegrationStep? CurrentStep { get; }
    internal int ProviderCalls { get; }
    internal int PromptOwners { get; }
    internal int CharacterOwners { get; }
    internal int MemoryPacketAdmissions { get; }
    internal int PlayerReplies { get; }
    internal int ProactiveNotes { get; }
    internal int ProactiveCooldownBlocks { get; }
    internal int QueueProposals { get; }
    internal int MotionExecutions { get; }
    internal int D022MotionExecutions { get; }
    internal int MotionDirectorExecutions { get; }

    internal D029PluginRuntimeSnapshot(
        D029SourceHostSnapshot source,
        D029IntegrationStep? currentStep,
        D029MutableStepCounters counters)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
        CurrentStep = currentStep;
        counters = counters
            ?? throw new ArgumentNullException(nameof(counters));
        ProviderCalls = counters.ProviderCalls;
        PromptOwners = counters.PromptOwners;
        CharacterOwners = counters.CharacterOwners;
        MemoryPacketAdmissions = counters.MemoryPacketAdmissions;
        PlayerReplies = counters.PlayerReplies;
        ProactiveNotes = counters.ProactiveNotes;
        ProactiveCooldownBlocks = counters.ProactiveCooldownBlocks;
        QueueProposals = counters.QueueProposals;
        MotionExecutions = counters.MotionExecutions;
        D022MotionExecutions = counters.D022MotionExecutions;
        MotionDirectorExecutions = counters.MotionDirectorExecutions;
    }
}

internal sealed class D029MutableStepCounters
{
    internal int ProviderCalls;
    internal int PromptOwners;
    internal int CharacterOwners;
    internal int MemoryPacketAdmissions;
    internal int PlayerReplies;
    internal int ProactiveNotes;
    internal int ProactiveCooldownBlocks;
    internal int QueueProposals;
    internal int MotionExecutions;
    internal int D022MotionExecutions;
    internal int MotionDirectorExecutions;
    internal int MaximumConcurrentProviders;
    internal int MaximumConcurrentProactive;
    internal int MaximumConcurrentMotion;

    internal D029MutableStepCounters Copy()
        => (D029MutableStepCounters)MemberwiseClone();

    internal D029IntegrationObservedCounters Freeze()
        => new(
            ProviderCalls,
            PromptOwners,
            CharacterOwners,
            MemoryPacketAdmissions,
            PlayerReplies,
            ProactiveNotes,
            QueueProposals,
            MotionExecutions,
            maximumConcurrentProviders: MaximumConcurrentProviders,
            maximumConcurrentProactive: MaximumConcurrentProactive,
            maximumConcurrentMotion: MaximumConcurrentMotion,
            proactiveCooldownBlocks: ProactiveCooldownBlocks,
            d022MotionExecutions: D022MotionExecutions,
            motionDirectorExecutions: MotionDirectorExecutions);
}

/// <summary>
/// Metadata-only coordinator for the accepted D-029 bounded live run.
/// It observes existing authoritative seams; it cannot create a Provider
/// request, Prompt, Memory write, Queue proposal, Motion or execution.
/// </summary>
internal sealed class D029AggressiveIntegrationPluginRuntime
{
    private readonly object _gate = new();
    private readonly D029AggressiveIntegrationSourceHost _source = new();
    private D029SourceHostConfiguration? _configuration;
    private D029IntegrationRunIdentity? _run;
    private D029MutableStepCounters _counters = new();
    private int _nextStep = 1;

    internal void Reconcile(D029SourceHostConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            _source.Reconcile(configuration);
            _configuration = configuration;
            if (!configuration.Enabled)
            {
                _run = null;
                _counters = new D029MutableStepCounters();
                _nextStep = 1;
            }
        }
    }

    internal D029SourceArmAdmission Arm(
        string runId,
        DateTimeOffset now)
    {
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (_configuration == null || !_configuration.Enabled)
                return D029SourceArmAdmission.Disabled;
            var run = new D029IntegrationRunIdentity(
                runId,
                _configuration.RuntimeIdentity.OrchestratorSessionId,
                _configuration.RuntimeIdentity,
                _configuration.ConfigurationFingerprint,
                _configuration.ConfigurationGeneration,
                now,
                now.Add(D029IntegrationCeilings.MaximumRunLifetime));
            var admission = _source.Arm(run, now);
            if (admission == D029SourceArmAdmission.Armed)
            {
                _run = run;
                _counters = new D029MutableStepCounters();
                _nextStep = 1;
            }
            return admission;
        }
    }

    internal D029PluginSignalAdmission Record(
        D029PluginSignal signal,
        CharacterRuntimeIdentity runtimeIdentity,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(typeof(D029PluginSignal), signal)
            || signal == D029PluginSignal.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(signal));
        }
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            var snapshot = _source.GetSnapshot();
            if (snapshot.Status == D029SourceHostStatus.Off)
                return D029PluginSignalAdmission.Disabled;
            if (_run == null
                || snapshot.Status is D029SourceHostStatus.ReadyToArm
                    or D029SourceHostStatus.Armed)
            {
                if (_run == null)
                    return D029PluginSignalAdmission.NotArmed;
            }
            if (snapshot.Status is D029SourceHostStatus.Stopped
                or D029SourceHostStatus.Completed
                or D029SourceHostStatus.Fault
                or D029SourceHostStatus.Capacity)
            {
                return D029PluginSignalAdmission.Terminal;
            }
            if (_run == null)
                return D029PluginSignalAdmission.NotArmed;
            if (!_run.RuntimeIdentity.Matches(runtimeIdentity))
                return D029PluginSignalAdmission.RuntimeMismatch;
            if (now >= _run.ExpiresAt)
                return D029PluginSignalAdmission.Expired;
            if (_nextStep > D029IntegrationCeilings.MaximumSteps)
                return D029PluginSignalAdmission.Terminal;

            Apply(signal, _counters);
            var step = (D029IntegrationStep)_nextStep;
            var budget = D029IntegrationPlan.Accepted.For(step);
            if (Exceeds(_counters, budget))
            {
                _source.Observe(
                    new D029IntegrationStepObservation(
                        _run,
                        step,
                        _nextStep,
                        now,
                        _counters.Freeze()));
                return D029PluginSignalAdmission.CounterMismatch;
            }
            if (!budget.Matches(_counters.Freeze()))
                return D029PluginSignalAdmission.Recorded;

            var admission = _source.Observe(
                new D029IntegrationStepObservation(
                    _run,
                    step,
                    _nextStep,
                    now,
                    _counters.Freeze()));
            _nextStep++;
            _counters = new D029MutableStepCounters();
            return admission switch
            {
                D029SourceObservationAdmission.Completed =>
                    D029PluginSignalAdmission.RunCompleted,
                D029SourceObservationAdmission.Accepted =>
                    D029PluginSignalAdmission.StepCompleted,
                _ => D029PluginSignalAdmission.Terminal
            };
        }
    }

    internal D029PluginRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var source = _source.GetSnapshot();
            D029IntegrationStep? step =
                _nextStep <= D029IntegrationCeilings.MaximumSteps
                    && source.Status is D029SourceHostStatus.Armed
                        or D029SourceHostStatus.Running
                    ? (D029IntegrationStep)_nextStep
                    : null;
            return new D029PluginRuntimeSnapshot(
                source,
                step,
                _counters.Copy());
        }
    }

    private static void Apply(
        D029PluginSignal signal,
        D029MutableStepCounters counters)
    {
        switch (signal)
        {
            case D029PluginSignal.PromptAndCharacterProjected:
                counters.PromptOwners++;
                counters.CharacterOwners++;
                break;
            case D029PluginSignal.MemoryPacketAdmitted:
                counters.MemoryPacketAdmissions++;
                break;
            case D029PluginSignal.ProviderCallStarted:
                counters.ProviderCalls++;
                counters.MaximumConcurrentProviders = Math.Max(
                    counters.MaximumConcurrentProviders,
                    1);
                break;
            case D029PluginSignal.PlayerReplyCompleted:
                counters.PlayerReplies++;
                break;
            case D029PluginSignal.ProactiveNoteCompleted:
                counters.ProactiveNotes++;
                counters.MaximumConcurrentProactive = Math.Max(
                    counters.MaximumConcurrentProactive,
                    1);
                break;
            case D029PluginSignal.ProactiveCooldownBlocked:
                counters.ProactiveCooldownBlocks++;
                break;
            case D029PluginSignal.D022QueueSubmitted:
                counters.QueueProposals++;
                break;
            case D029PluginSignal.D022MotionCompleted:
                counters.MotionExecutions++;
                counters.D022MotionExecutions++;
                counters.MaximumConcurrentMotion = Math.Max(
                    counters.MaximumConcurrentMotion,
                    1);
                break;
            case D029PluginSignal.MotionDirectorQueueSubmitted:
                counters.QueueProposals++;
                break;
            case D029PluginSignal.MotionDirectorMotionCompleted:
                counters.MotionExecutions++;
                counters.MotionDirectorExecutions++;
                counters.MaximumConcurrentMotion = Math.Max(
                    counters.MaximumConcurrentMotion,
                    1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(signal));
        }
    }

    private static bool Exceeds(
        D029MutableStepCounters counters,
        D029IntegrationStepBudget budget)
        => counters.ProviderCalls > budget.ProviderCalls
            || counters.PromptOwners > budget.PromptOwners
            || counters.CharacterOwners > budget.CharacterOwners
            || counters.MemoryPacketAdmissions
                > budget.MemoryPacketAdmissions
            || counters.PlayerReplies > budget.PlayerReplies
            || counters.ProactiveNotes > budget.ProactiveNotes
            || counters.ProactiveCooldownBlocks
                > budget.ProactiveCooldownBlocks
            || counters.QueueProposals > budget.QueueProposals
            || counters.MotionExecutions > budget.MotionExecutions
            || counters.D022MotionExecutions
                > budget.D022MotionExecutions
            || counters.MotionDirectorExecutions
                > budget.MotionDirectorExecutions;
}

internal static class D029AggressiveIntegrationStatusFormatter
{
    internal static string Format(D029PluginRuntimeSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var latest = snapshot.Source.Latest;
        return "D-029 激進整合測試  "
            + snapshot.Source.Status
            + "\n"
            + "三次 Provider・零重試・零 fallback"
            + "\n"
            + "目前步驟  "
            + (snapshot.CurrentStep?.ToString() ?? "-")
            + "\n"
            + "完成  "
            + snapshot.Source.RecordCount
            + "/"
            + D029IntegrationCeilings.MaximumSteps
            + "  Arm="
            + (snapshot.Source.ProcessArmConsumed ? "已使用" : "未使用")
            + "\n"
            + "本步  P"
            + snapshot.ProviderCalls
            + " PC"
            + snapshot.PromptOwners
            + "/"
            + snapshot.CharacterOwners
            + " M"
            + snapshot.MemoryPacketAdmissions
            + " R"
            + snapshot.PlayerReplies
            + " N"
            + snapshot.ProactiveNotes
            + " CD"
            + snapshot.ProactiveCooldownBlocks
            + " Q"
            + snapshot.QueueProposals
            + " X"
            + snapshot.MotionExecutions
            + "\n"
            + "最近  "
            + (latest == null
                ? "-"
                : latest.Step
                  + "/"
                  + latest.State
                  + "/"
                  + latest.StopReason)
            + "\n"
            + "權威輸出 0・內容不記錄";
    }
}
