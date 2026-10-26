using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal enum D029IntegrationStep
{
    Invalid = 0,
    PromptAndCharacter = 1,
    MemoryPacket = 2,
    PlayerReply = 3,
    ProactiveInteraction = 4,
    MotionAssist = 5,
    CombinedStress = 6
}

internal enum D029IntegrationRunState
{
    Invalid = 0,
    Off = 1,
    Armed = 2,
    Running = 3,
    Stopped = 4,
    Completed = 5,
    Fault = 6,
    Capacity = 7
}

internal enum D029IntegrationStopReason
{
    None = 0,
    RunIdentityMismatch = 1,
    InvalidSequence = 2,
    DuplicateStep = 3,
    ObservationNotYetValid = 4,
    ObservationExpired = 5,
    DuplicateOwnership = 6,
    DuplicateOutput = 7,
    WrongWriter = 8,
    AuthorityBoundaryCrossed = 9,
    CapacityAnomaly = 10,
    StaleOrLateAdmission = 11,
    CancellationFailure = 12,
    CrashOrUnhandledFault = 13,
    CounterMismatch = 14,
    UnexpectedPersistenceMutation = 15
}

internal static class D029IntegrationCeilings
{
    internal const int MaximumProviderCallsPerRun = 3;
    internal const int MaximumConcurrentProviderCalls = 1;
    internal const int MaximumConcurrentProactiveOccurrences = 1;
    internal const int MaximumConcurrentMotionAssists = 1;
    internal const int MaximumDiagnosticRecords = 128;
    internal const int MaximumSteps = 6;
    internal static TimeSpan MaximumRunLifetime { get; } =
        TimeSpan.FromMinutes(20);
    internal static int AutomaticRetries => 0;
    internal static int CrossProviderFallbacks => 0;
}

internal sealed class D029IntegrationRunIdentity
{
    internal string RunId { get; }
    internal string SessionId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ConfigurationFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset StartedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal D029IntegrationRunIdentity(
        string runId,
        string sessionId,
        CharacterRuntimeIdentity runtimeIdentity,
        string configurationFingerprint,
        long configurationGeneration,
        DateTimeOffset startedAt,
        DateTimeOffset expiresAt)
    {
        RunId = LivingContinuityContractGuard.Identifier(
            runId,
            nameof(runId));
        SessionId = LivingContinuityContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ConfigurationFingerprint =
            ProviderRegistryContractGuard.Sha256(
                configurationFingerprint,
                nameof(configurationFingerprint));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        StartedAt = DecisionCoreContractGuard.Timestamp(
            startedAt,
            nameof(startedAt));
        ExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= StartedAt
            || ExpiresAt - StartedAt
                > D029IntegrationCeilings.MaximumRunLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }
        if (!string.Equals(
                SessionId,
                RuntimeIdentity.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Run and runtime session identities must match.",
                nameof(sessionId));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/INTEGRATION-RUN/1",
            RunId,
            SessionId,
            RuntimeIdentity.RuntimeProfileId,
            RuntimeIdentity.RuntimeInstanceId,
            ProviderRegistryCanonicalHash.Number(
                RuntimeIdentity.Generation),
            RuntimeIdentity.OwnerIdentity,
            ConfigurationFingerprint,
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration),
            ProviderRegistryCanonicalHash.Timestamp(StartedAt),
            ProviderRegistryCanonicalHash.Timestamp(ExpiresAt));
    }

    internal bool Matches(D029IntegrationRunIdentity other)
        => other != null
            && string.Equals(
                Fingerprint,
                other.Fingerprint,
                StringComparison.Ordinal);
}

internal sealed class D029IntegrationStepBudget
{
    internal D029IntegrationStep Step { get; }
    internal int ProviderCalls { get; }
    internal int PromptOwners { get; }
    internal int CharacterOwners { get; }
    internal int MemoryPacketAdmissions { get; }
    internal int PlayerReplies { get; }
    internal int ProactiveNotes { get; }
    internal int QueueProposals { get; }
    internal int MotionExecutions { get; }
    internal int ProactiveCooldownBlocks { get; }
    internal int D022MotionExecutions { get; }
    internal int MotionDirectorExecutions { get; }
    internal string Fingerprint { get; }

    internal D029IntegrationStepBudget(
        D029IntegrationStep step,
        int providerCalls,
        int promptOwners,
        int characterOwners,
        int memoryPacketAdmissions,
        int playerReplies,
        int proactiveNotes,
        int queueProposals,
        int motionExecutions,
        int proactiveCooldownBlocks = 0,
        int d022MotionExecutions = 0,
        int motionDirectorExecutions = 0)
    {
        Step = Defined(step, nameof(step));
        ProviderCalls = NonNegative(
            providerCalls,
            nameof(providerCalls));
        PromptOwners = NonNegative(promptOwners, nameof(promptOwners));
        CharacterOwners = NonNegative(
            characterOwners,
            nameof(characterOwners));
        MemoryPacketAdmissions = NonNegative(
            memoryPacketAdmissions,
            nameof(memoryPacketAdmissions));
        PlayerReplies = NonNegative(
            playerReplies,
            nameof(playerReplies));
        ProactiveNotes = NonNegative(
            proactiveNotes,
            nameof(proactiveNotes));
        QueueProposals = NonNegative(
            queueProposals,
            nameof(queueProposals));
        MotionExecutions = NonNegative(
            motionExecutions,
            nameof(motionExecutions));
        ProactiveCooldownBlocks = NonNegative(
            proactiveCooldownBlocks,
            nameof(proactiveCooldownBlocks));
        D022MotionExecutions = NonNegative(
            d022MotionExecutions,
            nameof(d022MotionExecutions));
        MotionDirectorExecutions = NonNegative(
            motionDirectorExecutions,
            nameof(motionDirectorExecutions));
        if (D022MotionExecutions + MotionDirectorExecutions
            != MotionExecutions)
        {
            throw new ArgumentException(
                "Every Motion execution must have exactly one accepted "
                + "owner.");
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/STEP-BUDGET/1",
            ProviderRegistryCanonicalHash.Number((int)Step),
            ProviderRegistryCanonicalHash.Number(ProviderCalls),
            ProviderRegistryCanonicalHash.Number(PromptOwners),
            ProviderRegistryCanonicalHash.Number(CharacterOwners),
            ProviderRegistryCanonicalHash.Number(
                MemoryPacketAdmissions),
            ProviderRegistryCanonicalHash.Number(PlayerReplies),
            ProviderRegistryCanonicalHash.Number(ProactiveNotes),
            ProviderRegistryCanonicalHash.Number(QueueProposals),
            ProviderRegistryCanonicalHash.Number(MotionExecutions),
            ProviderRegistryCanonicalHash.Number(
                ProactiveCooldownBlocks),
            ProviderRegistryCanonicalHash.Number(D022MotionExecutions),
            ProviderRegistryCanonicalHash.Number(
                MotionDirectorExecutions));
    }

    internal bool Matches(D029IntegrationObservedCounters counters)
        => counters != null
            && ProviderCalls == counters.ProviderCalls
            && PromptOwners == counters.PromptOwners
            && CharacterOwners == counters.CharacterOwners
            && MemoryPacketAdmissions
                == counters.MemoryPacketAdmissions
            && PlayerReplies == counters.PlayerReplies
            && ProactiveNotes == counters.ProactiveNotes
            && QueueProposals == counters.QueueProposals
            && MotionExecutions == counters.MotionExecutions
            && ProactiveCooldownBlocks
                == counters.ProactiveCooldownBlocks
            && D022MotionExecutions == counters.D022MotionExecutions
            && MotionDirectorExecutions
                == counters.MotionDirectorExecutions;

    private static D029IntegrationStep Defined(
        D029IntegrationStep value,
        string name)
    {
        if (!Enum.IsDefined(typeof(D029IntegrationStep), value)
            || value == D029IntegrationStep.Invalid)
        {
            throw new ArgumentOutOfRangeException(name);
        }
        return value;
    }

    private static int NonNegative(int value, string name)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }
}

internal sealed class D029IntegrationPlan
{
    private readonly IReadOnlyDictionary<
        D029IntegrationStep,
        D029IntegrationStepBudget> _budgets;

    internal IReadOnlyList<D029IntegrationStepBudget> Steps { get; }
    internal int ExactProviderCalls { get; }
    internal string Fingerprint { get; }

    private D029IntegrationPlan(
        IReadOnlyList<D029IntegrationStepBudget> steps)
    {
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));
        if (steps.Count != D029IntegrationCeilings.MaximumSteps)
        {
            throw new ArgumentException(
                "The accepted integration plan requires exactly six steps.",
                nameof(steps));
        }
        var expected = Enum.GetValues<D029IntegrationStep>()
            .Where(item => item != D029IntegrationStep.Invalid)
            .OrderBy(item => (int)item)
            .ToArray();
        if (!steps.Select(item => item.Step).SequenceEqual(expected))
        {
            throw new ArgumentException(
                "Integration steps must use the accepted fixed order.",
                nameof(steps));
        }
        if (steps.Select(item => item.Step).Distinct().Count()
            != steps.Count)
        {
            throw new ArgumentException(
                "Integration steps cannot repeat.",
                nameof(steps));
        }
        var copied = steps.ToArray();
        Steps = new ReadOnlyCollection<D029IntegrationStepBudget>(
            copied);
        _budgets = new ReadOnlyDictionary<
            D029IntegrationStep,
            D029IntegrationStepBudget>(
            copied.ToDictionary(item => item.Step));
        ExactProviderCalls = copied.Sum(item => item.ProviderCalls);
        if (ExactProviderCalls
            != D029IntegrationCeilings.MaximumProviderCallsPerRun)
        {
            throw new ArgumentException(
                "The accepted plan must use exactly three Gemini calls.",
                nameof(steps));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/INTEGRATION-PLAN/1",
            copied.Select(item => item.Fingerprint).ToArray());
    }

    internal static D029IntegrationPlan Accepted { get; } =
        new(
            new[]
            {
                new D029IntegrationStepBudget(
                    D029IntegrationStep.PromptAndCharacter,
                    0, 1, 1, 0, 0, 0, 0, 0),
                new D029IntegrationStepBudget(
                    D029IntegrationStep.MemoryPacket,
                    0, 0, 0, 1, 0, 0, 0, 0),
                new D029IntegrationStepBudget(
                    D029IntegrationStep.PlayerReply,
                    1, 1, 1, 1, 1, 0, 0, 0),
                new D029IntegrationStepBudget(
                    D029IntegrationStep.ProactiveInteraction,
                    1, 1, 1, 1, 0, 1, 0, 0),
                new D029IntegrationStepBudget(
                    D029IntegrationStep.MotionAssist,
                    0, 0, 0, 0, 0, 0, 1, 1,
                    d022MotionExecutions: 1),
                new D029IntegrationStepBudget(
                    D029IntegrationStep.CombinedStress,
                    1, 1, 1, 1, 1, 0, 1, 1,
                    proactiveCooldownBlocks: 1,
                    motionDirectorExecutions: 1)
            });

    internal D029IntegrationStepBudget For(
        D029IntegrationStep step)
        => _budgets.TryGetValue(step, out var budget)
            ? budget
            : throw new ArgumentOutOfRangeException(nameof(step));
}

internal sealed class D029IntegrationObservedCounters
{
    internal int ProviderCalls { get; }
    internal int PromptOwners { get; }
    internal int CharacterOwners { get; }
    internal int MemoryPacketAdmissions { get; }
    internal int PlayerReplies { get; }
    internal int ProactiveNotes { get; }
    internal int QueueProposals { get; }
    internal int MotionExecutions { get; }
    internal int ProactiveCooldownBlocks { get; }
    internal int D022MotionExecutions { get; }
    internal int MotionDirectorExecutions { get; }
    internal int UnexpectedAuthorityOutputs { get; }
    internal int UnexpectedPersistenceWrites { get; }
    internal int MaximumConcurrentProviders { get; }
    internal int MaximumConcurrentProactive { get; }
    internal int MaximumConcurrentMotion { get; }
    internal int DiagnosticRecordCount { get; }
    internal string Fingerprint { get; }

    internal D029IntegrationObservedCounters(
        int providerCalls,
        int promptOwners,
        int characterOwners,
        int memoryPacketAdmissions,
        int playerReplies,
        int proactiveNotes,
        int queueProposals,
        int motionExecutions,
        int unexpectedAuthorityOutputs = 0,
        int unexpectedPersistenceWrites = 0,
        int maximumConcurrentProviders = 0,
        int maximumConcurrentProactive = 0,
        int maximumConcurrentMotion = 0,
        int diagnosticRecordCount = 1,
        int proactiveCooldownBlocks = 0,
        int d022MotionExecutions = 0,
        int motionDirectorExecutions = 0)
    {
        ProviderCalls = NonNegative(providerCalls, nameof(providerCalls));
        PromptOwners = NonNegative(promptOwners, nameof(promptOwners));
        CharacterOwners = NonNegative(
            characterOwners,
            nameof(characterOwners));
        MemoryPacketAdmissions = NonNegative(
            memoryPacketAdmissions,
            nameof(memoryPacketAdmissions));
        PlayerReplies = NonNegative(playerReplies, nameof(playerReplies));
        ProactiveNotes = NonNegative(
            proactiveNotes,
            nameof(proactiveNotes));
        QueueProposals = NonNegative(
            queueProposals,
            nameof(queueProposals));
        MotionExecutions = NonNegative(
            motionExecutions,
            nameof(motionExecutions));
        ProactiveCooldownBlocks = NonNegative(
            proactiveCooldownBlocks,
            nameof(proactiveCooldownBlocks));
        D022MotionExecutions = NonNegative(
            d022MotionExecutions,
            nameof(d022MotionExecutions));
        MotionDirectorExecutions = NonNegative(
            motionDirectorExecutions,
            nameof(motionDirectorExecutions));
        if (D022MotionExecutions + MotionDirectorExecutions
            != MotionExecutions)
        {
            throw new ArgumentException(
                "Observed Motion ownership does not match its execution "
                + "count.");
        }
        UnexpectedAuthorityOutputs = NonNegative(
            unexpectedAuthorityOutputs,
            nameof(unexpectedAuthorityOutputs));
        UnexpectedPersistenceWrites = NonNegative(
            unexpectedPersistenceWrites,
            nameof(unexpectedPersistenceWrites));
        MaximumConcurrentProviders = NonNegative(
            maximumConcurrentProviders,
            nameof(maximumConcurrentProviders));
        MaximumConcurrentProactive = NonNegative(
            maximumConcurrentProactive,
            nameof(maximumConcurrentProactive));
        MaximumConcurrentMotion = NonNegative(
            maximumConcurrentMotion,
            nameof(maximumConcurrentMotion));
        DiagnosticRecordCount = NonNegative(
            diagnosticRecordCount,
            nameof(diagnosticRecordCount));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/OBSERVED-COUNTERS/1",
            Values().Select(
                ProviderRegistryCanonicalHash.Number).ToArray());
    }

    internal IEnumerable<long> Values()
    {
        yield return ProviderCalls;
        yield return PromptOwners;
        yield return CharacterOwners;
        yield return MemoryPacketAdmissions;
        yield return PlayerReplies;
        yield return ProactiveNotes;
        yield return QueueProposals;
        yield return MotionExecutions;
        yield return ProactiveCooldownBlocks;
        yield return D022MotionExecutions;
        yield return MotionDirectorExecutions;
        yield return UnexpectedAuthorityOutputs;
        yield return UnexpectedPersistenceWrites;
        yield return MaximumConcurrentProviders;
        yield return MaximumConcurrentProactive;
        yield return MaximumConcurrentMotion;
        yield return DiagnosticRecordCount;
    }

    private static int NonNegative(int value, string name)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }
}

internal sealed class D029IntegrationStepObservation
{
    internal D029IntegrationRunIdentity Run { get; }
    internal D029IntegrationStep Step { get; }
    internal int Sequence { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal D029IntegrationObservedCounters Counters { get; }
    internal bool DuplicateOwnership { get; }
    internal bool DuplicateOutput { get; }
    internal bool WrongWriter { get; }
    internal bool StaleOrLateAdmission { get; }
    internal bool CancellationFailure { get; }
    internal bool CrashOrUnhandledFault { get; }
    internal bool CapacityAnomaly { get; }
    internal string Fingerprint { get; }

    internal D029IntegrationStepObservation(
        D029IntegrationRunIdentity run,
        D029IntegrationStep step,
        int sequence,
        DateTimeOffset observedAt,
        D029IntegrationObservedCounters counters,
        bool duplicateOwnership = false,
        bool duplicateOutput = false,
        bool wrongWriter = false,
        bool staleOrLateAdmission = false,
        bool cancellationFailure = false,
        bool crashOrUnhandledFault = false,
        bool capacityAnomaly = false)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        if (!Enum.IsDefined(typeof(D029IntegrationStep), step)
            || step == D029IntegrationStep.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        Step = step;
        Sequence = sequence;
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        Counters = counters
            ?? throw new ArgumentNullException(nameof(counters));
        DuplicateOwnership = duplicateOwnership;
        DuplicateOutput = duplicateOutput;
        WrongWriter = wrongWriter;
        StaleOrLateAdmission = staleOrLateAdmission;
        CancellationFailure = cancellationFailure;
        CrashOrUnhandledFault = crashOrUnhandledFault;
        CapacityAnomaly = capacityAnomaly;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/STEP-OBSERVATION/1",
            Run.Fingerprint,
            ProviderRegistryCanonicalHash.Number((int)Step),
            ProviderRegistryCanonicalHash.Number(Sequence),
            ProviderRegistryCanonicalHash.Timestamp(ObservedAt),
            Counters.Fingerprint,
            Bool(DuplicateOwnership),
            Bool(DuplicateOutput),
            Bool(WrongWriter),
            Bool(StaleOrLateAdmission),
            Bool(CancellationFailure),
            Bool(CrashOrUnhandledFault),
            Bool(CapacityAnomaly));
    }

    private static string Bool(bool value) => value ? "1" : "0";
}

internal sealed class D029IntegrationAuthorityCounters
{
    internal static D029IntegrationAuthorityCounters Zero { get; } =
        new();

    internal int Decisions => 0;
    internal int PromptWrites => 0;
    internal int MemoryWrites => 0;
    internal int CharacterWrites => 0;
    internal int RelationshipWrites => 0;
    internal int ProviderCalls => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal int ExecutionWrites => 0;
    internal int PersistenceWrites => 0;
    internal string Fingerprint { get; } =
        ProviderRegistryCanonicalHash.Of(
            "D029/ZERO-AUTHORITY/1",
            Enumerable.Repeat("0", 10).ToArray());

    private D029IntegrationAuthorityCounters()
    {
    }
}
