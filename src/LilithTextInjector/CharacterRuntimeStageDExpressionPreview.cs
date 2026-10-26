using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterExpressionPreviewStatus
{
    Off = 0,
    DecisionObserve = 1,
    Fault = 2,
    Capacity = 3
}

internal enum CharacterExpressionPreviewAdmission
{
    NotRunning = 0,
    PreviewRecorded = 1,
    DuplicatePair = 2,
    UnsupportedDecision = 3,
    DecisionExpired = 4,
    ProjectionExpired = 5,
    RuntimeIdentityMismatch = 6,
    CapacityReached = 7,
    Faulted = 8
}

internal enum CharacterExpressionComparisonLabel
{
    CharacterAwareBetter = 0,
    BaselineBetter = 1,
    Equivalent = 2,
    BothUnsuitable = 3,
    Uncertain = 4
}

internal enum CharacterExpressionComparisonAdmission
{
    Accepted = 0,
    PreviewMissing = 1,
    PreviewMismatch = 2,
    Duplicate = 3,
    CapacityReached = 4,
    NotRunning = 5
}

internal sealed class CharacterExpressionDecisionObservation
{
    internal InitiativeProducerSourceKind Source { get; }
    internal string CandidateId { get; }
    internal Guid DecisionId { get; }
    internal string? GoalId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal DecisionDisposition Disposition { get; }
    internal DecisionReason Reason { get; }
    internal InitiativePreviewBehaviorCategory BehaviorCategory { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ValidUntil { get; }

    internal CharacterExpressionDecisionObservation(
        InitiativeProducerSourceKind source,
        string candidateId,
        Guid decisionId,
        string? goalId,
        Guid situationId,
        long situationRevision,
        DecisionDisposition disposition,
        DecisionReason reason,
        InitiativePreviewBehaviorCategory behaviorCategory,
        DateTimeOffset observedAt,
        DateTimeOffset validUntil)
    {
        if (!Enum.IsDefined(typeof(InitiativeProducerSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (decisionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(decisionId));
        if (situationId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(situationId));
        if (!Enum.IsDefined(typeof(DecisionDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!Enum.IsDefined(typeof(DecisionReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (!Enum.IsDefined(
                typeof(InitiativePreviewBehaviorCategory),
                behaviorCategory))
        {
            throw new ArgumentOutOfRangeException(
                nameof(behaviorCategory));
        }

        Source = source;
        CandidateId = CharacterRuntimeContractGuard.Identifier(
            candidateId,
            nameof(candidateId));
        DecisionId = decisionId;
        GoalId = goalId == null
            ? null
            : CharacterRuntimeContractGuard.Identifier(
                goalId,
                nameof(goalId));
        SituationId = situationId;
        SituationRevision = CharacterRuntimeContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        Disposition = disposition;
        Reason = reason;
        BehaviorCategory = behaviorCategory;
        ObservedAt = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ValidUntil = CharacterRuntimeContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        if (ValidUntil <= ObservedAt)
            throw new ArgumentOutOfRangeException(nameof(validUntil));
        if (disposition is DecisionDisposition.Respond
                or DecisionDisposition.Act
                or DecisionDisposition.AskConsent
            && GoalId == null)
        {
            throw new ArgumentException(
                "An intervention decision observation must retain its "
                + "accepted Goal.",
                nameof(goalId));
        }
    }

    internal static CharacterExpressionDecisionObservation FromAccepted(
        InitiativeProducerSourceKind source,
        InitiativeCandidate? candidate,
        DecisionEvaluation evaluation,
        InitiativeDecisionPreview preview,
        DateTimeOffset observedAt)
    {
        if (evaluation == null)
            throw new ArgumentNullException(nameof(evaluation));
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        if (evaluation.Outcome.Invocation != null)
        {
            throw new ArgumentException(
                "Stage D cannot consume an invocation-pending Decision.",
                nameof(evaluation));
        }
        if (evaluation.Trace.DecisionId == Guid.Empty
            || evaluation.Trace.SnapshotId == Guid.Empty
            || evaluation.Trace.SnapshotSequence <= 0)
        {
            throw new ArgumentException(
                "Accepted Decision identity is incomplete.",
                nameof(evaluation));
        }

        return new CharacterExpressionDecisionObservation(
            source,
            candidate?.Id ?? "candidate-none",
            evaluation.Trace.DecisionId,
            evaluation.Outcome.SelectedGoalId,
            evaluation.Trace.SnapshotId,
            evaluation.Trace.SnapshotSequence,
            evaluation.Outcome.Disposition,
            evaluation.Outcome.Reason,
            preview.BehaviorCategory,
            observedAt,
            preview.ExpiresAt);
    }
}

internal sealed class CharacterExpressionPreviewProjection
{
    internal CharacterExpressionPreviewAdmission Admission { get; }
    internal CharacterExpressionPreviewRecord? Preview { get; }

    internal bool Accepted =>
        Admission == CharacterExpressionPreviewAdmission.PreviewRecorded;

    internal CharacterExpressionPreviewProjection(
        CharacterExpressionPreviewAdmission admission,
        CharacterExpressionPreviewRecord? preview)
    {
        if (!Enum.IsDefined(
                typeof(CharacterExpressionPreviewAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == CharacterExpressionPreviewAdmission.PreviewRecorded)
            != (preview != null))
        {
            throw new ArgumentException(
                "Only an accepted Stage D projection may carry a "
                + "Preview.",
                nameof(preview));
        }

        Admission = admission;
        Preview = preview;
    }
}

internal sealed class CharacterExpressionPreviewRecord
{
    internal string PreviewId { get; }
    internal CharacterExpressionDecisionObservation Decision { get; }
    internal CharacterSituationProjection Projection { get; }
    internal ExpressionIntent Intent { get; }
    internal string SelectedVariantId { get; }
    internal string BaselineVariantId => "style-balanced";
    internal DateTimeOffset CreatedAt { get; }
    internal CharacterInfluenceMode InfluenceMode =>
        CharacterInfluenceMode.DecisionObserve;
    internal bool Executable => false;
    internal bool Applied => false;

    internal CharacterExpressionPreviewRecord(
        string previewId,
        CharacterExpressionDecisionObservation decision,
        CharacterSituationProjection projection,
        ExpressionIntent intent,
        string selectedVariantId,
        DateTimeOffset createdAt)
    {
        PreviewId = CharacterRuntimeContractGuard.Identifier(
            previewId,
            nameof(previewId));
        Decision = decision
            ?? throw new ArgumentNullException(nameof(decision));
        Projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        SelectedVariantId = CharacterRuntimeContractGuard.Identifier(
            selectedVariantId,
            nameof(selectedVariantId));
        CreatedAt = CharacterRuntimeContractGuard.Timestamp(
            createdAt,
            nameof(createdAt));

        if (Intent.DecisionId != Decision.DecisionId
            || !string.Equals(
                Intent.GoalId,
                Decision.GoalId,
                StringComparison.Ordinal)
            || Intent.Disposition != Decision.Disposition
            || Intent.Reason != Decision.Reason
            || Intent.SituationId != Decision.SituationId
            || Intent.SituationRevision != Decision.SituationRevision
            || !Intent.RuntimeIdentity.Matches(
                Projection.RuntimeIdentity)
            || !Intent.EquivalentExpressions.SemanticVariantIds.Contains(
                SelectedVariantId,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Stage D Preview must preserve the exact Decision, "
                + "Situation, runtime and equivalent-expression set.");
        }
    }
}

internal static class CharacterExpressionPreviewProjector
{
    private static readonly string[] SemanticVariants =
    {
        "style-balanced",
        "style-direct",
        "style-restrained",
        "style-warm"
    };

    internal static CharacterExpressionPreviewProjection Project(
        CharacterExpressionDecisionObservation decision,
        CharacterSituationProjection projection,
        DateTimeOffset observedAt)
    {
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (decision.ValidUntil <= timestamp)
        {
            return Reject(
                CharacterExpressionPreviewAdmission.DecisionExpired);
        }
        if (projection.ExpiresAt <= timestamp)
        {
            return Reject(
                CharacterExpressionPreviewAdmission.ProjectionExpired);
        }
        if (!TryBinding(
                decision,
                out var capability,
                out var target,
                out var consent,
                out var intensity))
        {
            return Reject(
                CharacterExpressionPreviewAdmission.UnsupportedDecision);
        }

        var commitment = Hash(
            string.Join(
                "|",
                decision.DecisionId.ToString("N"),
                decision.GoalId ?? string.Empty,
                decision.SituationId.ToString("N"),
                decision.SituationRevision.ToString(
                    CultureInfo.InvariantCulture),
                (int)decision.Disposition,
                (int)decision.Reason,
                (int)capability,
                (int)target.Kind,
                target.TargetId,
                (int)consent,
                (int)intensity));
        var safety = new CharacterExpressionSafetyEnvelope(
            "safety-" + commitment[..16],
            consent,
            intensity);
        var equivalents = new EquivalentExpressionSet(
            "equivalent-" + commitment[..16],
            capability,
            target,
            commitment,
            consent,
            intensity,
            SemanticVariants);
        var selectedVariant = SelectVariant(projection);
        var validUntil = decision.ValidUntil < projection.ExpiresAt
            ? decision.ValidUntil
            : projection.ExpiresAt;
        var previewId = "expression-" + Hash(
            decision.DecisionId.ToString("N")
            + "|"
            + projection.SemanticProjectionFingerprint)[..20];
        var intent = new ExpressionIntent(
            "intent-" + previewId,
            decision.DecisionId,
            decision.GoalId,
            decision.Disposition,
            decision.Reason,
            decision.SituationId,
            decision.SituationRevision,
            projection.RuntimeIdentity,
            capability,
            target,
            commitment,
            safety,
            equivalents,
            validUntil);
        return new CharacterExpressionPreviewProjection(
            CharacterExpressionPreviewAdmission.PreviewRecorded,
            new CharacterExpressionPreviewRecord(
                previewId,
                decision,
                projection,
                intent,
                selectedVariant,
                timestamp));
    }

    private static bool TryBinding(
        CharacterExpressionDecisionObservation decision,
        out CharacterExpressionCapability capability,
        out CharacterExpressionTarget target,
        out CharacterConsentState consent,
        out CharacterBehaviorIntensity intensity)
    {
        capability = CharacterExpressionCapability.PlayerTriggeredResponse;
        target = new CharacterExpressionTarget(
            CharacterExpressionTargetKind.Player,
            "primary-player");
        consent = CharacterConsentState.NotApplicable;
        intensity = CharacterBehaviorIntensity.Low;

        if (decision.Disposition == DecisionDisposition.Silence)
        {
            capability = CharacterExpressionCapability.NonIntervention;
            target = new CharacterExpressionTarget(
                NonInterventionTarget.RemainSilent);
            intensity = CharacterBehaviorIntensity.Minimal;
            return true;
        }
        if (decision.Disposition == DecisionDisposition.Defer)
        {
            capability = CharacterExpressionCapability.NonIntervention;
            target = new CharacterExpressionTarget(
                NonInterventionTarget.DeferEngagement);
            intensity = CharacterBehaviorIntensity.Minimal;
            return true;
        }

        switch (decision.BehaviorCategory)
        {
            case InitiativePreviewBehaviorCategory.Acknowledge:
            case InitiativePreviewBehaviorCategory.ConversationOffer:
                if (decision.Disposition != DecisionDisposition.Respond)
                    return false;
                return true;
            case InitiativePreviewBehaviorCategory.ConsentQuestion:
                if (decision.Disposition
                    != DecisionDisposition.AskConsent)
                {
                    return false;
                }
                consent = CharacterConsentState.RequiredAndUnsatisfied;
                intensity = CharacterBehaviorIntensity.Minimal;
                return true;
            case InitiativePreviewBehaviorCategory.MotionOpportunity:
            case InitiativePreviewBehaviorCategory.RestOrWait:
                if (decision.Disposition != DecisionDisposition.Act)
                    return false;
                capability = CharacterExpressionCapability.Motion;
                target = new CharacterExpressionTarget(
                    CharacterExpressionTargetKind.Character,
                    "active-character");
                intensity = CharacterBehaviorIntensity.Low;
                return true;
            default:
                return false;
        }
    }

    private static string SelectVariant(
        CharacterSituationProjection projection)
    {
        var tendencies = projection.Profile.ExpressionTendencies;
        var affective = projection.Affective.State;
        var relationship = projection.Relationship.State;
        var communication = projection.Communication.State;
        var warm = Average(
            tendencies.Warmth,
            (affective.Mood + 1d) / 2d,
            relationship.Trust,
            relationship.Security);
        var direct = Average(
            tendencies.Directness,
            communication.Directness,
            communication.Signals.HasFlag(
                CommunicationContextSignal.Firm)
                ? 1d
                : 0d);
        var restrained = Average(
            tendencies.Restraint,
            affective.Irritation,
            affective.SleepPressure,
            affective.RecentInteractionPressure,
            projection.AmbiguousDomains
                == CharacterProjectionDomain.None
                    ? 0d
                    : 1d);
        var scores = new[]
        {
            (Variant: "style-balanced", Score: 0.5d),
            (Variant: "style-direct", Score: direct),
            (Variant: "style-restrained", Score: restrained),
            (Variant: "style-warm", Score: warm)
        };
        return scores
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Variant, StringComparer.Ordinal)
            .First()
            .Variant;
    }

    private static double Average(params double[] values)
        => values.Sum() / values.Length;

    private static CharacterExpressionPreviewProjection Reject(
        CharacterExpressionPreviewAdmission admission)
        => new(admission, preview: null);

    private static string Hash(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

internal sealed class CharacterExpressionComparisonRecord
{
    internal string PreviewId { get; }
    internal CharacterExpressionComparisonLabel Label { get; }
    internal DateTimeOffset RecordedAt { get; }
    internal bool MayInfluenceRuntime => false;
    internal bool MayTrain => false;

    internal CharacterExpressionComparisonRecord(
        string previewId,
        CharacterExpressionComparisonLabel label,
        DateTimeOffset recordedAt)
    {
        PreviewId = CharacterRuntimeContractGuard.Identifier(
            previewId,
            nameof(previewId));
        if (!Enum.IsDefined(
                typeof(CharacterExpressionComparisonLabel),
                label))
        {
            throw new ArgumentOutOfRangeException(nameof(label));
        }
        Label = label;
        RecordedAt = CharacterRuntimeContractGuard.Timestamp(
            recordedAt,
            nameof(recordedAt));
    }
}

internal sealed class CharacterExpressionPreviewSnapshot
{
    internal CharacterExpressionPreviewStatus Status { get; }
    internal bool Enabled { get; }
    internal CharacterInfluenceMode InfluenceMode { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal int AcceptedCount { get; }
    internal int RejectedCount { get; }
    internal int DuplicateCount { get; }
    internal int RetiredCount { get; }
    internal int RecordCount { get; }
    internal int ComparisonCount { get; }
    internal CharacterExpressionPreviewRecord? LatestPreview { get; }
    internal CharacterExpressionComparisonRecord? LatestComparison {
        get;
    }
    internal int ProductionCandidateOutputs => 0;
    internal int DecisionMutations => 0;
    internal int GoalMutations => 0;
    internal int AttentionMutations => 0;
    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ExecutableExpressionOutputs => 0;
    internal int MemoryCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;
    internal int CharacterStateWrites => 0;
    internal int RelationshipStateWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int PersistenceWrites => 0;
    internal int ExecutionOutputs => 0;

    internal CharacterExpressionPreviewSnapshot(
        CharacterExpressionPreviewStatus status,
        bool enabled,
        CharacterInfluenceMode influenceMode,
        CharacterRuntimeIdentity? runtimeIdentity,
        int acceptedCount,
        int rejectedCount,
        int duplicateCount,
        int retiredCount,
        int recordCount,
        int comparisonCount,
        CharacterExpressionPreviewRecord? latestPreview,
        CharacterExpressionComparisonRecord? latestComparison)
    {
        Status = status;
        Enabled = enabled;
        InfluenceMode = influenceMode;
        RuntimeIdentity = runtimeIdentity;
        AcceptedCount = acceptedCount;
        RejectedCount = rejectedCount;
        DuplicateCount = duplicateCount;
        RetiredCount = retiredCount;
        RecordCount = recordCount;
        ComparisonCount = comparisonCount;
        LatestPreview = latestPreview;
        LatestComparison = latestComparison;
    }

    internal static CharacterExpressionPreviewSnapshot Empty { get; } =
        new(
            CharacterExpressionPreviewStatus.Off,
            enabled: false,
            CharacterInfluenceMode.Off,
            runtimeIdentity: null,
            acceptedCount: 0,
            rejectedCount: 0,
            duplicateCount: 0,
            retiredCount: 0,
            recordCount: 0,
            comparisonCount: 0,
            latestPreview: null,
            latestComparison: null);
}

internal sealed class CharacterExpressionPreviewRuntimeHost
{
    internal const int MaximumRecords = 128;
    internal const int MaximumComparisons = 128;

    private readonly object _gate = new();
    private readonly List<CharacterExpressionPreviewRecord> _records =
        new();
    private readonly List<CharacterExpressionComparisonRecord>
        _comparisons = new();
    private readonly HashSet<Guid> _consumedDecisions = new();
    private CharacterExpressionPreviewStatus _status =
        CharacterExpressionPreviewStatus.Off;
    private bool _enabled;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private int _acceptedCount;
    private int _rejectedCount;
    private int _duplicateCount;
    private int _retiredCount;

    internal void Reconcile(
        bool enabled,
        bool sessionOpen,
        CharacterRuntimeIdentity? runtimeIdentity,
        DateTimeOffset occurredAt)
    {
        lock (_gate)
        {
            if (!enabled || !sessionOpen)
            {
                ResetToOff();
                return;
            }
            _ = CharacterRuntimeContractGuard.Timestamp(
                occurredAt,
                nameof(occurredAt));
            if (runtimeIdentity == null)
                throw new ArgumentNullException(nameof(runtimeIdentity));
            if (_enabled
                && _runtimeIdentity!.Matches(runtimeIdentity)
                && _status != CharacterExpressionPreviewStatus.Fault)
            {
                return;
            }

            _retiredCount += _records.Count;
            _records.Clear();
            _comparisons.Clear();
            _consumedDecisions.Clear();
            _runtimeIdentity = runtimeIdentity;
            _enabled = true;
            _status = CharacterExpressionPreviewStatus.DecisionObserve;
            _acceptedCount = 0;
            _rejectedCount = 0;
            _duplicateCount = 0;
        }
    }

    internal CharacterExpressionPreviewAdmission Observe(
        CharacterExpressionDecisionObservation decision,
        CharacterSituationProjection projection,
        DateTimeOffset occurredAt)
    {
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            if (!_enabled
                || _status == CharacterExpressionPreviewStatus.Off)
            {
                return CharacterExpressionPreviewAdmission.NotRunning;
            }
            if (_status == CharacterExpressionPreviewStatus.Fault)
                return CharacterExpressionPreviewAdmission.Faulted;
            if (_status == CharacterExpressionPreviewStatus.Capacity)
                return CharacterExpressionPreviewAdmission.CapacityReached;
            if (!_runtimeIdentity!.Matches(projection.RuntimeIdentity))
            {
                _rejectedCount++;
                return CharacterExpressionPreviewAdmission
                    .RuntimeIdentityMismatch;
            }
            if (_consumedDecisions.Contains(decision.DecisionId))
            {
                _duplicateCount++;
                return CharacterExpressionPreviewAdmission.DuplicatePair;
            }
            if (_records.Count >= MaximumRecords)
            {
                _status = CharacterExpressionPreviewStatus.Capacity;
                return CharacterExpressionPreviewAdmission.CapacityReached;
            }

            _consumedDecisions.Add(decision.DecisionId);
            var result = CharacterExpressionPreviewProjector.Project(
                decision,
                projection,
                timestamp);
            if (!result.Accepted)
            {
                _rejectedCount++;
                return result.Admission;
            }

            _records.Add(result.Preview!);
            _acceptedCount++;
            if (_records.Count >= MaximumRecords)
                _status = CharacterExpressionPreviewStatus.Capacity;
            return CharacterExpressionPreviewAdmission.PreviewRecorded;
        }
    }

    internal CharacterExpressionComparisonAdmission SubmitComparison(
        string previewId,
        CharacterExpressionComparisonLabel label,
        DateTimeOffset occurredAt)
    {
        var normalizedPreviewId =
            CharacterRuntimeContractGuard.Identifier(
                previewId,
                nameof(previewId));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            if (!_enabled)
                return CharacterExpressionComparisonAdmission.NotRunning;
            var preview = _records.LastOrDefault(
                item => string.Equals(
                    item.PreviewId,
                    normalizedPreviewId,
                    StringComparison.Ordinal));
            if (preview == null)
                return CharacterExpressionComparisonAdmission.PreviewMissing;
            if (_records.Count == 0
                || !ReferenceEquals(preview, _records[^1]))
            {
                return CharacterExpressionComparisonAdmission
                    .PreviewMismatch;
            }
            if (_comparisons.Any(
                    item => string.Equals(
                        item.PreviewId,
                        normalizedPreviewId,
                        StringComparison.Ordinal)))
            {
                return CharacterExpressionComparisonAdmission.Duplicate;
            }
            if (_comparisons.Count >= MaximumComparisons)
            {
                return CharacterExpressionComparisonAdmission
                    .CapacityReached;
            }
            _comparisons.Add(
                new CharacterExpressionComparisonRecord(
                    normalizedPreviewId,
                    label,
                    timestamp));
            return CharacterExpressionComparisonAdmission.Accepted;
        }
    }

    internal void StopByFault(DateTimeOffset occurredAt)
    {
        _ = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            _retiredCount += _records.Count;
            _records.Clear();
            _comparisons.Clear();
            _consumedDecisions.Clear();
            _enabled = false;
            _status = CharacterExpressionPreviewStatus.Fault;
        }
    }

    internal CharacterExpressionPreviewSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (!_enabled
                && _status == CharacterExpressionPreviewStatus.Off)
            {
                return CharacterExpressionPreviewSnapshot.Empty;
            }
            return new CharacterExpressionPreviewSnapshot(
                _status,
                _enabled,
                _enabled
                    ? CharacterInfluenceMode.DecisionObserve
                    : CharacterInfluenceMode.Off,
                _runtimeIdentity,
                _acceptedCount,
                _rejectedCount,
                _duplicateCount,
                _retiredCount,
                _records.Count,
                _comparisons.Count,
                _records.Count == 0 ? null : _records[^1],
                _comparisons.Count == 0 ? null : _comparisons[^1]);
        }
    }

    private void ResetToOff()
    {
        _records.Clear();
        _comparisons.Clear();
        _consumedDecisions.Clear();
        _status = CharacterExpressionPreviewStatus.Off;
        _enabled = false;
        _runtimeIdentity = null;
        _acceptedCount = 0;
        _rejectedCount = 0;
        _duplicateCount = 0;
        _retiredCount = 0;
    }
}
