using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum LivingProactiveNoteCancellationStage
{
    PostOwnershipDecision = 0,
    OwnershipPersistence = 1,
    GeneratingStatePersistence = 2,
    BridgePreparation = 3,
    PromptRootOwnership = 4,
    PromptComposition = 5,
    ProviderDispatchPreparation = 6,
    ActiveReconciliation = 7,
    ProviderCompletion = 8,
    CompletionProjection = 9,
    NativeDeliveryAdmission = 10,
    NativeWrite = 11
}

internal enum LivingProactiveNoteCancellationReason
{
    None = 0,
    DecisionRejected = 1,
    AuthorizationMissing = 2,
    OwnershipPersistenceFailed = 3,
    BridgePreparationRejected = 4,
    PromptRootOwnershipRejected = 5,
    PromptCompositionRejected = 6,
    ProviderDispatchRejected = 7,
    ConfigurationGenerationChanged = 8,
    RuntimeIdentityChanged = 9,
    EpochChanged = 10,
    MasterDisabled = 11,
    SourceDisabled = 12,
    DirectPlayerControl = 13,
    ConversationBusy = 14,
    PendingExecution = 15,
    QuietPeriod = 16,
    CancellationRequested = 17,
    CompletionDeadlineExpired = 18,
    SituationFingerprintChanged = 19,
    EvaluationRateLimited = 20,
    ActiveCapacityReached = 21,
    ProcessCapacityReached = 22,
    RollingCapacityReached = 23,
    CooldownActive = 24,
    CandidateExpired = 25,
    OwnershipConflict = 26,
    BrainOwnedNoFallback = 27,
    LegacyAlreadyCompleted = 28,
    DuplicateTerminal = 29,
    ProviderCancelled = 30,
    ProviderFaulted = 31,
    ProviderEmptyResponse = 32,
    StaleProviderResult = 33,
    CompletionRejected = 34,
    NativeActiveOperationMismatch = 35,
    NativeWriteFailed = 36,
    NativeOccurrenceMismatch = 37,
    GeneratingStatePersistenceFailed = 38
}

internal enum LivingProactiveNotePrimaryPendingExecutionSubgate
{
    None = 0,
    ActionShowcasePendingOperation = 1,
    ActionShowcaseWindowWalkContinuation = 2,
    NativeStateMachineLocked = 3,
    NativeActionAnimationPlaying = 4
}

internal static class
    LivingProactiveNotePrimaryPendingExecutionSubgateClassifier
{
    internal static LivingProactiveNotePrimaryPendingExecutionSubgate
        FromFacts(
        bool actionShowcasePendingOperation,
        bool actionShowcaseWindowWalkContinuation,
        bool nativeStateMachineLocked,
        bool nativeActionAnimationPlaying)
    {
        if (actionShowcasePendingOperation)
        {
            return LivingProactiveNotePrimaryPendingExecutionSubgate
                .ActionShowcasePendingOperation;
        }
        if (actionShowcaseWindowWalkContinuation)
        {
            return LivingProactiveNotePrimaryPendingExecutionSubgate
                .ActionShowcaseWindowWalkContinuation;
        }
        if (nativeStateMachineLocked)
        {
            return LivingProactiveNotePrimaryPendingExecutionSubgate
                .NativeStateMachineLocked;
        }
        if (nativeActionAnimationPlaying)
        {
            return LivingProactiveNotePrimaryPendingExecutionSubgate
                .NativeActionAnimationPlaying;
        }
        return LivingProactiveNotePrimaryPendingExecutionSubgate.None;
    }

    internal static bool IsKnown(
        LivingProactiveNotePrimaryPendingExecutionSubgate subgate)
        => Enum.IsDefined(
            typeof(LivingProactiveNotePrimaryPendingExecutionSubgate),
            subgate);

    internal static LivingProactiveNotePrimaryPendingExecutionSubgate
        ForTerminalReason(
            LivingProactiveNoteCancellationReason reason,
            LivingProactiveNotePrimaryPendingExecutionSubgate
                observedSubgate)
    {
        if (!IsKnown(observedSubgate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedSubgate));
        }
        return reason
            == LivingProactiveNoteCancellationReason.PendingExecution
                ? observedSubgate
                : LivingProactiveNotePrimaryPendingExecutionSubgate.None;
    }

    internal static string Format(
        LivingProactiveNotePrimaryPendingExecutionSubgate subgate)
    {
        if (!IsKnown(subgate))
            throw new ArgumentOutOfRangeException(nameof(subgate));
        return subgate.ToString();
    }
}

internal enum LivingProactiveNoteTelemetryAdmission
{
    Recorded = 0,
    DuplicateTerminal = 1,
    CapacityReached = 2
}

internal sealed class LivingProactiveNoteCancellationGateFacts
{
    internal bool ExactActiveOperation { get; }
    internal bool ConfigurationMatches { get; }
    internal bool RuntimeMatches { get; }
    internal bool EpochMatches { get; }
    internal bool MasterEnabled { get; }
    internal bool SourceEnabled { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }
    internal bool IsCancellationRequested { get; }
    internal bool IsBeforeCompletionDeadline { get; }
    internal bool SituationMatches { get; }

    internal LivingProactiveNoteCancellationGateFacts(
        bool exactActiveOperation,
        bool configurationMatches,
        bool runtimeMatches,
        bool epochMatches,
        bool masterEnabled,
        bool sourceEnabled,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool isCancellationRequested,
        bool isBeforeCompletionDeadline,
        bool situationMatches)
    {
        ExactActiveOperation = exactActiveOperation;
        ConfigurationMatches = configurationMatches;
        RuntimeMatches = runtimeMatches;
        EpochMatches = epochMatches;
        MasterEnabled = masterEnabled;
        SourceEnabled = sourceEnabled;
        IsAvatarUnderDirectPlayerControl =
            isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
        IsCancellationRequested = isCancellationRequested;
        IsBeforeCompletionDeadline = isBeforeCompletionDeadline;
        SituationMatches = situationMatches;
    }
}

internal static class LivingProactiveNoteCancellationClassifier
{
    internal static LivingProactiveNoteCancellationReason
        FromActiveCancellation(
            bool isCancellationRequested,
            bool isBeforeCompletionDeadline)
    {
        if (!isBeforeCompletionDeadline)
        {
            return LivingProactiveNoteCancellationReason
                .CompletionDeadlineExpired;
        }
        return isCancellationRequested
            ? LivingProactiveNoteCancellationReason.CancellationRequested
            : LivingProactiveNoteCancellationReason.None;
    }

    internal static LivingProactiveNoteCancellationReason
        FromProviderCompletion(
            DateTimeOffset observedAt,
            DateTimeOffset completionExpiresAt,
            bool isCancellationRequested,
            bool providerReportedCancellation,
            bool providerFaulted,
            bool responseWasEmpty)
    {
        if ((isCancellationRequested || providerReportedCancellation)
            && observedAt >= completionExpiresAt)
        {
            return LivingProactiveNoteCancellationReason
                .CompletionDeadlineExpired;
        }
        if (isCancellationRequested)
        {
            return LivingProactiveNoteCancellationReason
                .CancellationRequested;
        }
        if (providerReportedCancellation)
        {
            return LivingProactiveNoteCancellationReason
                .ProviderCancelled;
        }
        if (providerFaulted)
            return LivingProactiveNoteCancellationReason.ProviderFaulted;
        if (responseWasEmpty)
        {
            return LivingProactiveNoteCancellationReason
                .ProviderEmptyResponse;
        }
        return LivingProactiveNoteCancellationReason.None;
    }

    internal static LivingProactiveNoteCancellationReason FromDecision(
        ProactiveRespondResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        if (result.Admission
                == ProactiveRespondAdmission.AuthorizationPrepared
            && result.Authorization == null)
        {
            return LivingProactiveNoteCancellationReason
                .AuthorizationMissing;
        }
        if (result.Admission
                == ProactiveRespondAdmission.DecisionDidNotRespond
            && result.Evaluation != null)
        {
            return FromDecisionReason(
                result.Evaluation.Outcome.Reason);
        }

        return result.Admission switch
        {
            ProactiveRespondAdmission.MasterDisabled =>
                LivingProactiveNoteCancellationReason.MasterDisabled,
            ProactiveRespondAdmission.SourceDisabled =>
                LivingProactiveNoteCancellationReason.SourceDisabled,
            ProactiveRespondAdmission.RuntimeMismatch =>
                LivingProactiveNoteCancellationReason
                    .RuntimeIdentityChanged,
            ProactiveRespondAdmission.SituationMismatch =>
                LivingProactiveNoteCancellationReason
                    .SituationFingerprintChanged,
            ProactiveRespondAdmission.EpochMismatch =>
                LivingProactiveNoteCancellationReason.EpochChanged,
            ProactiveRespondAdmission.Expired =>
                LivingProactiveNoteCancellationReason.CandidateExpired,
            ProactiveRespondAdmission.OwnershipConflict =>
                LivingProactiveNoteCancellationReason.OwnershipConflict,
            ProactiveRespondAdmission.LegacyAlreadyCompleted =>
                LivingProactiveNoteCancellationReason
                    .LegacyAlreadyCompleted,
            ProactiveRespondAdmission.BrainOwnedNoFallback =>
                LivingProactiveNoteCancellationReason
                    .BrainOwnedNoFallback,
            ProactiveRespondAdmission.EvaluationRateLimited =>
                LivingProactiveNoteCancellationReason
                    .EvaluationRateLimited,
            ProactiveRespondAdmission.CooldownActive =>
                LivingProactiveNoteCancellationReason.CooldownActive,
            ProactiveRespondAdmission.RollingCapacityReached =>
                LivingProactiveNoteCancellationReason
                    .RollingCapacityReached,
            ProactiveRespondAdmission.ProcessCapacityReached =>
                LivingProactiveNoteCancellationReason
                    .ProcessCapacityReached,
            ProactiveRespondAdmission.ActiveCapacityReached =>
                LivingProactiveNoteCancellationReason
                    .ActiveCapacityReached,
            ProactiveRespondAdmission.DuplicateTerminal =>
                LivingProactiveNoteCancellationReason.DuplicateTerminal,
            _ => LivingProactiveNoteCancellationReason.DecisionRejected
        };
    }

    internal static LivingProactiveNoteCancellationReason FromGate(
        LivingProactiveNoteCancellationGateFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));
        if (!facts.ExactActiveOperation)
        {
            return LivingProactiveNoteCancellationReason
                .NativeActiveOperationMismatch;
        }
        if (!facts.ConfigurationMatches)
        {
            return LivingProactiveNoteCancellationReason
                .ConfigurationGenerationChanged;
        }
        if (!facts.RuntimeMatches)
        {
            return LivingProactiveNoteCancellationReason
                .RuntimeIdentityChanged;
        }
        if (!facts.EpochMatches)
            return LivingProactiveNoteCancellationReason.EpochChanged;
        if (!facts.MasterEnabled)
            return LivingProactiveNoteCancellationReason.MasterDisabled;
        if (!facts.SourceEnabled)
            return LivingProactiveNoteCancellationReason.SourceDisabled;
        if (facts.IsAvatarUnderDirectPlayerControl)
        {
            return LivingProactiveNoteCancellationReason
                .DirectPlayerControl;
        }
        if (facts.IsConversationBusy)
        {
            return LivingProactiveNoteCancellationReason
                .ConversationBusy;
        }
        if (facts.HasPendingExecution)
            return LivingProactiveNoteCancellationReason.PendingExecution;
        if (facts.IsQuietPeriod)
            return LivingProactiveNoteCancellationReason.QuietPeriod;
        if (!facts.IsBeforeCompletionDeadline)
        {
            return LivingProactiveNoteCancellationReason
                .CompletionDeadlineExpired;
        }
        if (facts.IsCancellationRequested)
        {
            return LivingProactiveNoteCancellationReason
                .CancellationRequested;
        }
        if (!facts.SituationMatches)
        {
            return LivingProactiveNoteCancellationReason
                .SituationFingerprintChanged;
        }
        return LivingProactiveNoteCancellationReason.None;
    }

    internal static LivingProactiveNoteCancellationReason
        FromFinalDelivery(
            LivingProactiveNoteFinalDeliveryFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));
        return FromGate(
            new LivingProactiveNoteCancellationGateFacts(
                facts.ExactActiveOperation,
                facts.ConfigurationMatches,
                facts.RuntimeMatches,
                facts.EpochMatches,
                facts.MasterEnabled,
                sourceEnabled: facts.SourceEnabled,
                facts.IsAvatarUnderDirectPlayerControl,
                facts.IsConversationBusy,
                facts.HasPendingExecution,
                facts.IsQuietPeriod,
                facts.IsCancelled,
                facts.IsBeforeCompletionDeadline,
                facts.SituationMatches));
    }

    private static LivingProactiveNoteCancellationReason
        FromDecisionReason(DecisionReason reason)
        => reason switch
        {
            DecisionReason.DirectPlayerControl =>
                LivingProactiveNoteCancellationReason
                    .DirectPlayerControl,
            DecisionReason.ConversationBusy =>
                LivingProactiveNoteCancellationReason.ConversationBusy,
            DecisionReason.PendingExecution =>
                LivingProactiveNoteCancellationReason.PendingExecution,
            DecisionReason.QuietPeriod =>
                LivingProactiveNoteCancellationReason.QuietPeriod,
            DecisionReason.InitiativeCooldown =>
                LivingProactiveNoteCancellationReason.CooldownActive,
            DecisionReason.SnapshotExpired =>
                LivingProactiveNoteCancellationReason.CandidateExpired,
            _ => LivingProactiveNoteCancellationReason.DecisionRejected
        };
}

internal sealed class LivingProactiveNoteCancellationDiagnostic
{
    internal DateTimeOffset OccurredAt { get; }
    internal Guid OccurrenceId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal long RuntimeGeneration { get; }
    internal long ConfigurationGeneration { get; }
    internal LivingProactiveNoteCancellationStage Stage { get; }
    internal LivingProactiveNoteCancellationReason Reason { get; }
    internal bool ProviderExecutionStarted { get; }
    internal ProactiveRespondAdmission? DecisionAdmission { get; }
    internal DecisionReason? DecisionReason { get; }
    internal LivingProactiveNoteBridgeAdmission? BridgeAdmission { get; }
    internal LivingPromptCompositionAdmission? PromptAdmission { get; }
    internal LivingProactiveNotePrimaryPendingExecutionSubgate
        PrimaryPendingExecutionSubgate { get; }

    internal LivingProactiveNoteCancellationDiagnostic(
        DateTimeOffset occurredAt,
        Guid occurrenceId,
        Guid situationId,
        long situationRevision,
        long runtimeGeneration,
        long configurationGeneration,
        LivingProactiveNoteCancellationStage stage,
        LivingProactiveNoteCancellationReason reason,
        bool providerExecutionStarted,
        ProactiveRespondAdmission? decisionAdmission = null,
        DecisionReason? decisionReason = null,
        LivingProactiveNoteBridgeAdmission? bridgeAdmission = null,
        LivingPromptCompositionAdmission? promptAdmission = null,
        LivingProactiveNotePrimaryPendingExecutionSubgate
            primaryPendingExecutionSubgate =
                LivingProactiveNotePrimaryPendingExecutionSubgate.None)
    {
        if (occurredAt == default)
            throw new ArgumentOutOfRangeException(nameof(occurredAt));
        if (occurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Occurrence identity cannot be empty.",
                nameof(occurrenceId));
        if (situationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(situationId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        if (!Enum.IsDefined(typeof(LivingProactiveNoteCancellationStage),
                stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        if (!Enum.IsDefined(typeof(LivingProactiveNoteCancellationReason),
                reason)
            || reason == LivingProactiveNoteCancellationReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if (decisionAdmission.HasValue
            && !Enum.IsDefined(
                typeof(ProactiveRespondAdmission),
                decisionAdmission.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decisionAdmission));
        }
        if (decisionReason.HasValue
            && !Enum.IsDefined(
                typeof(DecisionReason),
                decisionReason.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(decisionReason));
        }
        if (bridgeAdmission.HasValue
            && !Enum.IsDefined(
                typeof(LivingProactiveNoteBridgeAdmission),
                bridgeAdmission.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(bridgeAdmission));
        }
        if (promptAdmission.HasValue
            && !Enum.IsDefined(
                typeof(LivingPromptCompositionAdmission),
                promptAdmission.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(promptAdmission));
        }
        if (!LivingProactiveNotePrimaryPendingExecutionSubgateClassifier
                .IsKnown(primaryPendingExecutionSubgate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(primaryPendingExecutionSubgate));
        }
        var isPendingExecution =
            reason == LivingProactiveNoteCancellationReason.PendingExecution;
        var hasPrimaryPendingExecutionSubgate =
            primaryPendingExecutionSubgate
            != LivingProactiveNotePrimaryPendingExecutionSubgate.None;
        if (isPendingExecution != hasPrimaryPendingExecutionSubgate)
        {
            throw new ArgumentException(
                "PendingExecution reason and primary subgate metadata must "
                + "be present together.",
                nameof(primaryPendingExecutionSubgate));
        }

        OccurredAt = occurredAt.ToUniversalTime();
        OccurrenceId = occurrenceId;
        SituationId = situationId;
        SituationRevision = situationRevision;
        RuntimeGeneration = runtimeGeneration;
        ConfigurationGeneration = configurationGeneration;
        Stage = stage;
        Reason = reason;
        ProviderExecutionStarted = providerExecutionStarted;
        DecisionAdmission = decisionAdmission;
        DecisionReason = decisionReason;
        BridgeAdmission = bridgeAdmission;
        PromptAdmission = promptAdmission;
        PrimaryPendingExecutionSubgate = primaryPendingExecutionSubgate;
    }
}

internal sealed class LivingProactiveNoteCancellationTelemetry
{
    internal const int Capacity = 32;

    private readonly object _gate = new();
    private readonly List<LivingProactiveNoteCancellationDiagnostic>
        _records = new(Capacity);
    private readonly HashSet<Guid> _terminalOccurrences = new();

    internal int Count
    {
        get
        {
            lock (_gate)
                return _records.Count;
        }
    }

    internal IReadOnlyList<LivingProactiveNoteCancellationDiagnostic>
        Snapshot()
    {
        lock (_gate)
            return Array.AsReadOnly(_records.ToArray());
    }

    internal LivingProactiveNoteTelemetryAdmission TryRecordOnce(
        LivingProactiveNoteCancellationDiagnostic diagnostic)
    {
        if (diagnostic == null)
            throw new ArgumentNullException(nameof(diagnostic));
        lock (_gate)
        {
            if (_terminalOccurrences.Contains(diagnostic.OccurrenceId))
            {
                return LivingProactiveNoteTelemetryAdmission
                    .DuplicateTerminal;
            }
            if (_records.Count >= Capacity)
            {
                return LivingProactiveNoteTelemetryAdmission
                    .CapacityReached;
            }
            _terminalOccurrences.Add(diagnostic.OccurrenceId);
            _records.Add(diagnostic);
            return LivingProactiveNoteTelemetryAdmission.Recorded;
        }
    }
}

internal static class LivingProactiveNoteCancellationLogFormatter
{
    internal static string Format(
        LivingProactiveNoteCancellationDiagnostic diagnostic)
    {
        if (diagnostic == null)
            throw new ArgumentNullException(nameof(diagnostic));
        var occurrence = diagnostic.OccurrenceId.ToString("N");
        return "D-025 F3 terminal metadata: stage="
            + diagnostic.Stage
            + " reason="
            + diagnostic.Reason
            + " primaryPendingExecutionSubgate="
            + LivingProactiveNotePrimaryPendingExecutionSubgateClassifier
                .Format(
                    diagnostic.PrimaryPendingExecutionSubgate)
            + " providerStarted="
            + (diagnostic.ProviderExecutionStarted ? "true" : "false")
            + " occurrence="
            + occurrence[^8..]
            + " situationRevision="
            + diagnostic.SituationRevision
            + " runtimeGeneration="
            + diagnostic.RuntimeGeneration
            + " configurationGeneration="
            + diagnostic.ConfigurationGeneration
            + " decisionAdmission="
            + (diagnostic.DecisionAdmission?.ToString() ?? "None")
            + " decisionReason="
            + (diagnostic.DecisionReason?.ToString() ?? "None")
            + " bridgeAdmission="
            + (diagnostic.BridgeAdmission?.ToString() ?? "None")
            + " promptAdmission="
            + (diagnostic.PromptAdmission?.ToString() ?? "None");
    }
}
