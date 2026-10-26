using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterLanguageStyleStageFCStatus
{
    Off = 0,
    ReadyToArm = 1,
    Armed = 2,
    PreviewReady = 3,
    Fault = 4,
    Capacity = 5
}

internal enum CharacterLanguageStyleStageFCAdmission
{
    NotRunning = 0,
    ArmAccepted = 1,
    AlreadyArmed = 2,
    ProcessArmAlreadyConsumed = 3,
    PreviewRecorded = 4,
    LabelAccepted = 5,
    LabelMissing = 6,
    LabelDuplicate = 7,
    CapacityReached = 8,
    ObservationIneligible = 9,
    CorrelationMismatch = 10,
    AuthorizationExpired = 11,
    RuntimeMismatch = 12,
    SessionMismatch = 13,
    SituationMismatch = 14,
    ProjectionUnavailable = 15,
    ProjectionExpired = 16,
    DuplicatePreview = 17,
    Disabled = 18,
    RestartCleared = 19,
    Faulted = 20
}

internal enum CharacterLanguageStyleStageFCComparisonLabel
{
    CharacterStyleBetter = 0,
    LegacyStyleBetter = 1,
    Equivalent = 2,
    BothUnsuitable = 3,
    Uncertain = 4
}

internal sealed class CharacterLanguageStyleStageFCConfiguration
{
    internal const int MaximumCapacity = 32;
    internal static CharacterLanguageStyleStageFCConfiguration Off {
        get;
    } = new(enabled: false, capacity: MaximumCapacity);

    internal bool Enabled { get; }
    internal int Capacity { get; }

    internal CharacterLanguageStyleStageFCConfiguration(
        bool enabled,
        int capacity)
    {
        if (capacity <= 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Enabled = enabled;
        Capacity = capacity;
    }
}

internal sealed class CharacterLanguageStyleStageFCPreview
{
    internal const string EquivalentExpressionSetId =
        "player-response-language-style-equivalent-set.v1";

    internal string PreviewId { get; }
    internal string CorrelationId { get; }
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal PlayerTriggeredResponseSurface InputSurface { get; }
    internal Guid RequestSituationId { get; }
    internal long RequestSituationRevision { get; }
    internal long ProjectionSituationRevision { get; }
    internal long ProjectionRevision { get; }
    internal string SessionId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ProviderRouteId { get; }
    internal string PromptBaselineSha256 { get; }
    internal int PromptBaselineCharacters { get; }
    internal ExistingCharacterPromptInfluenceMode ExistingMode { get; }
    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal string SemanticProjectionFingerprint { get; }
    internal string ContextFingerprint { get; }
    internal CharacterLanguageStyleId BaselineStyle =>
        CharacterLanguageStyleId.Balanced;
    internal CharacterLanguageStyleId CharacterStyle { get; }
    internal string BaselineStyleId =>
        CharacterLanguageStyleIds.ToStableId(BaselineStyle);
    internal string CharacterStyleId =>
        CharacterLanguageStyleIds.ToStableId(CharacterStyle);
    internal string EquivalentSetId => EquivalentExpressionSetId;
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal bool ContainsPlayerText => false;
    internal bool ContainsPromptText => false;
    internal bool ContainsReplyText => false;
    internal bool AppliesOwnerLease => false;
    internal bool AppliesStyle => false;
    internal bool WritesPrompt => false;
    internal bool GeneratesLanguage => false;
    internal bool Executable => false;

    internal CharacterLanguageStyleStageFCPreview(
        string previewId,
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        CharacterLanguageStyleStageFBRequestObservation observation,
        CharacterSituationProjection projection,
        string contextFingerprint,
        CharacterLanguageStyleId characterStyle,
        DateTimeOffset observedAt)
    {
        if (correlation == null)
            throw new ArgumentNullException(nameof(correlation));
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        if (!Enum.IsDefined(typeof(CharacterLanguageStyleId), characterStyle))
            throw new ArgumentOutOfRangeException(nameof(characterStyle));
        if (observation.Admission
            != CharacterLanguageStyleStageFBAdmission.RequestEligible)
        {
            throw new ArgumentException(
                "F-C Preview requires one eligible F-B observation.",
                nameof(observation));
        }
        if (!string.Equals(
                correlation.CorrelationId,
                observation.CorrelationId,
                StringComparison.Ordinal)
            || correlation.Authorization.AuthorizationId
                != observation.AuthorizationId
            || correlation.Authorization.TurnId != observation.TurnId)
        {
            throw new ArgumentException(
                "F-C correlation and request observation must match.",
                nameof(observation));
        }
        if (!correlation.Authorization.RuntimeIdentity.Matches(
                projection.RuntimeIdentity))
        {
            throw new ArgumentException(
                "F-C projection must belong to the same immutable "
                + "Character runtime.",
                nameof(projection));
        }

        PreviewId = CharacterLanguageStyleStageFContractGuard.Identifier(
            previewId,
            nameof(previewId));
        CorrelationId = correlation.CorrelationId;
        AuthorizationId = observation.AuthorizationId;
        TurnId = observation.TurnId;
        InputSurface = observation.InputSurface;
        RequestSituationId =
            correlation.Authorization.SituationId;
        RequestSituationRevision =
            correlation.Authorization.SituationRevision;
        ProjectionSituationRevision = projection.SituationRevision;
        ProjectionRevision = projection.ProjectionRevision;
        SessionId = correlation.Authorization.SessionId;
        RuntimeIdentity = correlation.Authorization.RuntimeIdentity;
        ProviderRouteId = observation.ProviderRouteId;
        PromptBaselineSha256 = observation.PromptBaselineSha256;
        PromptBaselineCharacters = observation.PromptBaselineCharacters;
        ExistingMode = observation.ExistingMode;
        ProfileId = projection.Profile.Identity.ProfileId;
        ProfileVersion = projection.Profile.Identity.ProfileVersion;
        SemanticProjectionFingerprint =
            projection.SemanticProjectionFingerprint;
        ContextFingerprint =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                contextFingerprint,
                nameof(contextFingerprint));
        CharacterStyle = characterStyle;
        ObservedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                observedAt,
                nameof(observedAt));
        ValidUntil = correlation.ValidUntil < projection.ExpiresAt
            ? correlation.ValidUntil
            : projection.ExpiresAt;
        if (ObservedAt >= ValidUntil)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
    }
}

internal sealed class CharacterLanguageStyleStageFCLabelRecord
{
    internal string PreviewId { get; }
    internal CharacterLanguageStyleStageFCComparisonLabel Label {
        get;
    }
    internal DateTimeOffset RecordedAt { get; }
    internal bool MayInfluenceRuntime => false;
    internal bool MayTrain => false;
    internal bool MayPersist => false;

    internal CharacterLanguageStyleStageFCLabelRecord(
        string previewId,
        CharacterLanguageStyleStageFCComparisonLabel label,
        DateTimeOffset recordedAt)
    {
        PreviewId = CharacterLanguageStyleStageFContractGuard.Identifier(
            previewId,
            nameof(previewId));
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFCComparisonLabel),
                label))
        {
            throw new ArgumentOutOfRangeException(nameof(label));
        }
        Label = label;
        RecordedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                recordedAt,
                nameof(recordedAt));
    }
}

internal sealed class CharacterLanguageStyleStageFCAuthorityCounters
{
    internal static CharacterLanguageStyleStageFCAuthorityCounters Zero {
        get;
    } = new();

    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal int OwnerLeasesIssued => 0;
    internal int DirectivesCreated => 0;
    internal int GeneratedLanguageOutputs => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int DecisionOutputs => 0;
    internal int GoalOutputs => 0;
    internal int AttentionOutputs => 0;
    internal int CharacterStateWrites => 0;
    internal int RelationshipStateWrites => 0;
    internal int PersistenceWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int VoiceOutputs => 0;
    internal int ToolOutputs => 0;
    internal int ExecutionOutputs => 0;
    internal bool IsZero =>
        PromptWrites == 0
        && ReplyOutputs == 0
        && ProviderCalls == 0
        && ProviderRetries == 0
        && OwnerLeasesIssued == 0
        && DirectivesCreated == 0
        && GeneratedLanguageOutputs == 0
        && MemoryCalls == 0
        && EvidenceCalls == 0
        && ReasoningCalls == 0
        && DecisionOutputs == 0
        && GoalOutputs == 0
        && AttentionOutputs == 0
        && CharacterStateWrites == 0
        && RelationshipStateWrites == 0
        && PersistenceWrites == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && VoiceOutputs == 0
        && ToolOutputs == 0
        && ExecutionOutputs == 0;

    private CharacterLanguageStyleStageFCAuthorityCounters()
    {
    }
}

internal sealed class CharacterLanguageStyleStageFCSnapshot
{
    internal CharacterLanguageStyleStageFCStatus Status { get; }
    internal bool Enabled { get; }
    internal bool Armed { get; }
    internal bool ProcessArmConsumed { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal int PreviewCount { get; }
    internal int LabelCount { get; }
    internal int RejectedCount { get; }
    internal int RetiredCount { get; }
    internal CharacterLanguageStyleStageFCPreview? LatestPreview {
        get;
    }
    internal CharacterLanguageStyleStageFCLabelRecord? LatestLabel {
        get;
    }
    internal CharacterLanguageStyleStageFCAdmission LastAdmission {
        get;
    }
    internal CharacterLanguageStyleStageFCAuthorityCounters
        AuthorityCounters { get; }
    internal bool IsEmpty =>
        PreviewCount == 0
        && LabelCount == 0
        && LatestPreview == null
        && LatestLabel == null;

    internal CharacterLanguageStyleStageFCSnapshot(
        CharacterLanguageStyleStageFCStatus status,
        bool enabled,
        bool armed,
        bool processArmConsumed,
        CharacterRuntimeIdentity? runtimeIdentity,
        int previewCount,
        int labelCount,
        int rejectedCount,
        int retiredCount,
        CharacterLanguageStyleStageFCPreview? latestPreview,
        CharacterLanguageStyleStageFCLabelRecord? latestLabel,
        CharacterLanguageStyleStageFCAdmission lastAdmission)
    {
        Status = status;
        Enabled = enabled;
        Armed = armed;
        ProcessArmConsumed = processArmConsumed;
        RuntimeIdentity = runtimeIdentity;
        PreviewCount = previewCount;
        LabelCount = labelCount;
        RejectedCount = rejectedCount;
        RetiredCount = retiredCount;
        LatestPreview = latestPreview;
        LatestLabel = latestLabel;
        LastAdmission = lastAdmission;
        AuthorityCounters =
            CharacterLanguageStyleStageFCAuthorityCounters.Zero;
    }

    internal static CharacterLanguageStyleStageFCSnapshot Empty {
        get;
    } = new(
        CharacterLanguageStyleStageFCStatus.Off,
        enabled: false,
        armed: false,
        processArmConsumed: false,
        runtimeIdentity: null,
        previewCount: 0,
        labelCount: 0,
        rejectedCount: 0,
        retiredCount: 0,
        latestPreview: null,
        latestLabel: null,
        CharacterLanguageStyleStageFCAdmission.RestartCleared);
}

internal static class CharacterLanguageStyleStageFCStyleSelector
{
    internal static CharacterLanguageStyleId Select(
        CharacterSituationProjection projection)
    {
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
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
            (Style: CharacterLanguageStyleId.Balanced, Score: 0.5d),
            (Style: CharacterLanguageStyleId.Direct, Score: direct),
            (Style: CharacterLanguageStyleId.Restrained, Score: restrained),
            (Style: CharacterLanguageStyleId.Warm, Score: warm)
        };
        return scores
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Style)
            .First()
            .Style;
    }

    private static double Average(params double[] values)
        => values.Sum() / values.Length;
}

internal sealed class CharacterLanguageStyleStageFCPreviewHost
{
    internal const int MaximumPreviews =
        CharacterLanguageStyleStageFCConfiguration.MaximumCapacity;
    internal const int MaximumLabels =
        CharacterLanguageStyleStageFCConfiguration.MaximumCapacity;

    private readonly object _gate = new();
    private readonly List<CharacterLanguageStyleStageFCPreview>
        _previews = new();
    private readonly List<CharacterLanguageStyleStageFCLabelRecord>
        _labels = new();
    private readonly HashSet<Guid> _consumedAuthorizations = new();
    private CharacterLanguageStyleStageFCConfiguration _configuration =
        CharacterLanguageStyleStageFCConfiguration.Off;
    private CharacterLanguageStyleStageFCStatus _status =
        CharacterLanguageStyleStageFCStatus.Off;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private bool _enabled;
    private bool _armed;
    private bool _processArmConsumed;
    private int _rejectedCount;
    private int _retiredCount;
    private CharacterLanguageStyleStageFCAdmission _lastAdmission =
        CharacterLanguageStyleStageFCAdmission.RestartCleared;

    internal bool IsArmed
    {
        get
        {
            lock (_gate)
                return _enabled && _armed;
        }
    }

    internal void Reconcile(
        CharacterLanguageStyleStageFCConfiguration configuration,
        bool sessionOpen,
        CharacterRuntimeIdentity? runtimeIdentity,
        DateTimeOffset occurredAt)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            if (!configuration.Enabled || !sessionOpen)
            {
                ResetToOff(preserveProcessArm: true);
                _lastAdmission =
                    CharacterLanguageStyleStageFCAdmission.Disabled;
                return;
            }
            _ = CharacterLanguageStyleStageFContractGuard.Timestamp(
                occurredAt,
                nameof(occurredAt));
            if (runtimeIdentity == null)
                throw new ArgumentNullException(nameof(runtimeIdentity));
            if (_enabled
                && _runtimeIdentity!.Matches(runtimeIdentity)
                && _status != CharacterLanguageStyleStageFCStatus.Fault)
            {
                _configuration = configuration;
                return;
            }

            _retiredCount += _previews.Count;
            _previews.Clear();
            _labels.Clear();
            _consumedAuthorizations.Clear();
            _configuration = configuration;
            _runtimeIdentity = runtimeIdentity;
            _enabled = true;
            _armed = false;
            _status = _processArmConsumed
                ? CharacterLanguageStyleStageFCStatus.Capacity
                : CharacterLanguageStyleStageFCStatus.ReadyToArm;
        }
    }

    internal CharacterLanguageStyleStageFCAdmission Arm()
    {
        lock (_gate)
        {
            if (!_enabled
                || _status == CharacterLanguageStyleStageFCStatus.Off)
            {
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission.NotRunning);
            }
            if (_status == CharacterLanguageStyleStageFCStatus.Fault)
            {
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission.Faulted);
            }
            if (_armed)
            {
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission.AlreadyArmed);
            }
            if (_processArmConsumed)
            {
                _status = CharacterLanguageStyleStageFCStatus.Capacity;
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission
                        .ProcessArmAlreadyConsumed);
            }

            _processArmConsumed = true;
            _armed = true;
            _status = CharacterLanguageStyleStageFCStatus.Armed;
            return SetAdmission(
                CharacterLanguageStyleStageFCAdmission.ArmAccepted);
        }
    }

    internal CharacterLanguageStyleStageFCAdmission TryPreview(
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        CharacterLanguageStyleStageFBRequestObservation observation,
        CharacterSituationProjection projection,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        DateTimeOffset observedAt,
        out CharacterLanguageStyleStageFCPreview? preview)
    {
        if (correlation == null)
            throw new ArgumentNullException(nameof(correlation));
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        if (activeRuntimeIdentity == null)
            throw new ArgumentNullException(nameof(activeRuntimeIdentity));
        preview = null;
        var timestamp =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                observedAt,
                nameof(observedAt));

        lock (_gate)
        {
            if (!_enabled || !_armed)
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission.NotRunning);
            }
            if (_previews.Count >= _configuration.Capacity)
            {
                _armed = false;
                _status = CharacterLanguageStyleStageFCStatus.Capacity;
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .CapacityReached);
            }
            if (observation.Admission
                != CharacterLanguageStyleStageFBAdmission.RequestEligible)
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .ObservationIneligible);
            }
            if (!string.Equals(
                    correlation.CorrelationId,
                    observation.CorrelationId,
                    StringComparison.Ordinal)
                || correlation.Authorization.AuthorizationId
                    != observation.AuthorizationId
                || correlation.Authorization.TurnId
                    != observation.TurnId)
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .CorrelationMismatch);
            }
            if (timestamp >= correlation.ValidUntil)
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .AuthorizationExpired);
            }
            if (!_runtimeIdentity!.Matches(activeRuntimeIdentity)
                || !correlation.Authorization.RuntimeIdentity.Matches(
                    activeRuntimeIdentity)
                || !projection.RuntimeIdentity.Matches(
                    activeRuntimeIdentity))
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .RuntimeMismatch);
            }
            if (!string.Equals(
                    correlation.Authorization.SessionId,
                    activeSessionId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .SessionMismatch);
            }
            if (correlation.Authorization.SituationId
                    != activeSituationId
                || correlation.Authorization.SituationRevision
                    != activeSituationRevision)
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .SituationMismatch);
            }
            if (projection.ExpiresAt <= timestamp
                || string.IsNullOrWhiteSpace(
                    projection.SemanticProjectionFingerprint))
            {
                return Reject(
                    projection.ExpiresAt <= timestamp
                        ? CharacterLanguageStyleStageFCAdmission
                            .ProjectionExpired
                        : CharacterLanguageStyleStageFCAdmission
                            .ProjectionUnavailable);
            }
            if (!_consumedAuthorizations.Add(
                    correlation.Authorization.AuthorizationId))
            {
                return Reject(
                    CharacterLanguageStyleStageFCAdmission
                        .DuplicatePreview);
            }

            var context =
                CharacterPersonalityStyleProjector.Classify(projection);
            var style =
                CharacterLanguageStyleStageFCStyleSelector.Select(
                    projection);
            var previewId = "stage-fc-" + Hash(
                string.Join(
                    "|",
                    correlation.CorrelationId,
                    observation.AuthorizationId.ToString("N"),
                    projection.SemanticProjectionFingerprint,
                    CharacterLanguageStyleIds.ToStableId(style)))[..20];
            preview = new CharacterLanguageStyleStageFCPreview(
                previewId,
                correlation,
                observation,
                projection,
                context.ContextFingerprint,
                style,
                timestamp);
            _previews.Add(preview);
            _armed = false;
            _status = CharacterLanguageStyleStageFCStatus.PreviewReady;
            return SetAdmission(
                CharacterLanguageStyleStageFCAdmission.PreviewRecorded);
        }
    }

    internal CharacterLanguageStyleStageFCAdmission SubmitLabel(
        string previewId,
        CharacterLanguageStyleStageFCComparisonLabel label,
        DateTimeOffset recordedAt)
    {
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFCComparisonLabel),
                label))
        {
            throw new ArgumentOutOfRangeException(nameof(label));
        }
        lock (_gate)
        {
            if (!_enabled)
            {
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission.NotRunning);
            }
            var preview = _previews.LastOrDefault(item =>
                string.Equals(
                    item.PreviewId,
                    previewId,
                    StringComparison.Ordinal));
            if (preview == null)
            {
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission.LabelMissing);
            }
            if (_labels.Any(item =>
                    string.Equals(
                        item.PreviewId,
                        previewId,
                        StringComparison.Ordinal)))
            {
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission.LabelDuplicate);
            }
            if (_labels.Count >= _configuration.Capacity)
            {
                _status = CharacterLanguageStyleStageFCStatus.Capacity;
                return SetAdmission(
                    CharacterLanguageStyleStageFCAdmission
                        .CapacityReached);
            }
            _labels.Add(
                new CharacterLanguageStyleStageFCLabelRecord(
                    previewId,
                    label,
                    recordedAt));
            return SetAdmission(
                CharacterLanguageStyleStageFCAdmission.LabelAccepted);
        }
    }

    internal CharacterLanguageStyleStageFCSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (!_enabled
                && _status == CharacterLanguageStyleStageFCStatus.Off
                && !_processArmConsumed
                && _retiredCount == 0)
            {
                return CharacterLanguageStyleStageFCSnapshot.Empty;
            }
            return new CharacterLanguageStyleStageFCSnapshot(
                _status,
                _enabled,
                _armed,
                _processArmConsumed,
                _runtimeIdentity,
                _previews.Count,
                _labels.Count,
                _rejectedCount,
                _retiredCount,
                _previews.Count == 0 ? null : _previews[^1],
                _labels.Count == 0 ? null : _labels[^1],
                _lastAdmission);
        }
    }

    internal void StopByFault()
    {
        lock (_gate)
        {
            ResetToOff(preserveProcessArm: true);
            _status = CharacterLanguageStyleStageFCStatus.Fault;
            _lastAdmission =
                CharacterLanguageStyleStageFCAdmission.Faulted;
        }
    }

    internal void Restart()
    {
        lock (_gate)
        {
            ResetToOff(preserveProcessArm: false);
            _lastAdmission =
                CharacterLanguageStyleStageFCAdmission.RestartCleared;
        }
    }

    private CharacterLanguageStyleStageFCAdmission Reject(
        CharacterLanguageStyleStageFCAdmission admission)
    {
        _rejectedCount++;
        return SetAdmission(admission);
    }

    private CharacterLanguageStyleStageFCAdmission SetAdmission(
        CharacterLanguageStyleStageFCAdmission admission)
    {
        _lastAdmission = admission;
        return admission;
    }

    private void ResetToOff(bool preserveProcessArm)
    {
        _retiredCount += _previews.Count;
        _previews.Clear();
        _labels.Clear();
        _consumedAuthorizations.Clear();
        _configuration =
            CharacterLanguageStyleStageFCConfiguration.Off;
        _runtimeIdentity = null;
        _enabled = false;
        _armed = false;
        if (!preserveProcessArm)
        {
            _processArmConsumed = false;
            _rejectedCount = 0;
            _retiredCount = 0;
        }
        _status = CharacterLanguageStyleStageFCStatus.Off;
    }

    private static string Hash(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
