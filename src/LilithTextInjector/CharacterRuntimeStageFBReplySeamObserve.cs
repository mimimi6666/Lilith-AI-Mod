using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

[Flags]
internal enum CharacterLanguageStyleStageFBExclusion
{
    None = 0,
    LocalDeterministicReply = 1 << 0,
    BuiltInAgentShadow = 1 << 1,
    DesktopTools = 1 << 2,
    ForcedSearch = 1 << 3,
    ModelMotionEnvelope = 1 << 4,
    JapaneseBilingualJson = 1 << 5
}

internal enum CharacterLanguageStyleStageFBStatus
{
    Off = 0,
    Observe = 1,
    Fault = 2,
    Capacity = 3
}

internal enum CharacterLanguageStyleStageFBAdmission
{
    TurnObserved = 0,
    RequestEligible = 1,
    LegacyAssistConflict = 2,
    FirstLiveExcluded = 3,
    Disabled = 4,
    CapacityReached = 5,
    DuplicateCorrelationRejected = 6,
    CorrelationNotFound = 7,
    CorrelationAlreadyTerminal = 8,
    AuthorizationExpired = 9,
    RuntimeMismatch = 10,
    SessionMismatch = 11,
    SituationMismatch = 12,
    ProviderRouteInvalid = 13,
    PromptMetadataInvalid = 14,
    RestartCleared = 15
}

internal enum CharacterLanguageStyleStageFBRecordKind
{
    Turn = 0,
    Request = 1,
    Lifecycle = 2
}

internal sealed class CharacterLanguageStyleStageFBConfiguration
{
    internal const int MaximumCapacity = 128;
    internal static CharacterLanguageStyleStageFBConfiguration Off {
        get;
    } = new(enabled: false, capacity: MaximumCapacity);

    internal bool Enabled { get; }
    internal int Capacity { get; }

    internal CharacterLanguageStyleStageFBConfiguration(
        bool enabled,
        int capacity)
    {
        if (capacity <= 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Enabled = enabled;
        Capacity = capacity;
    }
}

internal static class CharacterLanguageStyleStageFBIdentity
{
    internal static Guid Create(
        string purpose,
        CharacterRuntimeIdentity runtimeIdentity,
        long sourceSequence)
    {
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        var normalizedPurpose =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                purpose,
                nameof(purpose));
        var sequence =
            CharacterLanguageStyleStageFContractGuard.Positive(
                sourceSequence,
                nameof(sourceSequence));
        var material = string.Join(
            "|",
            normalizedPurpose,
            runtimeIdentity.RuntimeProfileId,
            runtimeIdentity.RuntimeInstanceId,
            runtimeIdentity.Generation.ToString(
                CultureInfo.InvariantCulture),
            runtimeIdentity.OrchestratorSessionId,
            runtimeIdentity.OwnerIdentity,
            sequence.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}

internal sealed class CharacterLanguageStyleStageFBTurnCorrelation
{
    internal string CorrelationId { get; }
    internal long SourceSequence { get; }
    internal PlayerTriggeredRespondAuthorization Authorization { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ValidUntil => Authorization.ValidUntil;
    internal bool ContainsPlayerText => false;
    internal bool ContainsPromptText => false;
    internal bool ContainsReplyText => false;

    internal CharacterLanguageStyleStageFBTurnCorrelation(
        string correlationId,
        long sourceSequence,
        PlayerTriggeredRespondAuthorization authorization,
        DateTimeOffset observedAt)
    {
        CorrelationId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                correlationId,
                nameof(correlationId));
        SourceSequence =
            CharacterLanguageStyleStageFContractGuard.Positive(
                sourceSequence,
                nameof(sourceSequence));
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        if (!Authorization.IsExplicitLegacyPlayerRequest
            || Authorization.DecisionId.HasValue
            || Authorization.GoalId != null)
        {
            throw new ArgumentException(
                "F-B real player-turn correlation must remain explicit "
                + "legacy authority without a synthetic Decision.",
                nameof(authorization));
        }
        ObservedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                observedAt,
                nameof(observedAt));
        if (ObservedAt < Authorization.IssuedAt
            || ObservedAt >= Authorization.ValidUntil)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        }
    }
}

internal sealed class CharacterLanguageStyleStageFBRequestObservation
{
    internal string CorrelationId { get; }
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal PlayerTriggeredResponseSurface InputSurface { get; }
    internal string ProviderRouteId { get; }
    internal string PromptBaselineSha256 { get; }
    internal int PromptBaselineCharacters { get; }
    internal ExistingCharacterPromptInfluenceMode ExistingMode { get; }
    internal CharacterLanguageStyleStageFBExclusion Exclusions { get; }
    internal CharacterLanguageStyleStageFBAdmission Admission { get; }
    internal LanguageStylePromptOwner FutureOwnerObservation { get; }
    internal bool LegacyCandidateObserved => true;
    internal bool CharacterRuntimeCandidateObserved =>
        Admission
            == CharacterLanguageStyleStageFBAdmission.RequestEligible;
    internal DateTimeOffset ObservedAt { get; }
    internal bool ContainsPlayerText => false;
    internal bool ContainsPromptText => false;
    internal bool ContainsReplyText => false;
    internal bool AppliesOwnerLease => false;
    internal bool AppliesStyle => false;

    internal CharacterLanguageStyleStageFBRequestObservation(
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        string providerRouteId,
        string promptBaselineSha256,
        int promptBaselineCharacters,
        ExistingCharacterPromptInfluenceMode existingMode,
        CharacterLanguageStyleStageFBExclusion exclusions,
        CharacterLanguageStyleStageFBAdmission admission,
        LanguageStylePromptOwner futureOwnerObservation,
        DateTimeOffset observedAt)
    {
        if (correlation == null)
            throw new ArgumentNullException(nameof(correlation));
        if (!Enum.IsDefined(
                typeof(ExistingCharacterPromptInfluenceMode),
                existingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(existingMode));
        }
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFBAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (!Enum.IsDefined(
                typeof(LanguageStylePromptOwner),
                futureOwnerObservation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(futureOwnerObservation));
        }
        if (promptBaselineCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(promptBaselineCharacters));
        }

        CorrelationId = correlation.CorrelationId;
        AuthorizationId = correlation.Authorization.AuthorizationId;
        TurnId = correlation.Authorization.TurnId;
        InputSurface = correlation.Authorization.InputSurface;
        ProviderRouteId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                providerRouteId,
                nameof(providerRouteId));
        PromptBaselineSha256 =
            CharacterLanguageStyleStageFContractGuard.Sha256Identifier(
                promptBaselineSha256,
                nameof(promptBaselineSha256));
        PromptBaselineCharacters = promptBaselineCharacters;
        ExistingMode = existingMode;
        Exclusions = exclusions;
        Admission = admission;
        FutureOwnerObservation = futureOwnerObservation;
        ObservedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                observedAt,
                nameof(observedAt));

        if (Admission
                == CharacterLanguageStyleStageFBAdmission
                    .LegacyAssistConflict
            && (ExistingMode
                    != ExistingCharacterPromptInfluenceMode.Assist
                || FutureOwnerObservation
                    != LanguageStylePromptOwner.LegacyOwned))
        {
            throw new ArgumentException(
                "Legacy Assist conflict must remain Legacy-owned.",
                nameof(admission));
        }
        if (Admission
                == CharacterLanguageStyleStageFBAdmission.RequestEligible
            && (Exclusions
                    != CharacterLanguageStyleStageFBExclusion.None
                || ExistingMode
                    == ExistingCharacterPromptInfluenceMode.Assist
                || FutureOwnerObservation
                    != LanguageStylePromptOwner
                        .CharacterRuntimeOwned))
        {
            throw new ArgumentException(
                "An eligible observation must have no first-live "
                + "exclusion or Legacy Assist conflict.",
                nameof(admission));
        }
        if (Admission
                == CharacterLanguageStyleStageFBAdmission
                    .FirstLiveExcluded
            && Exclusions
                == CharacterLanguageStyleStageFBExclusion.None)
        {
            throw new ArgumentException(
                "Excluded observation requires a typed exclusion.",
                nameof(exclusions));
        }
    }
}

internal sealed class CharacterLanguageStyleStageFBRecord
{
    internal long Sequence { get; }
    internal CharacterLanguageStyleStageFBRecordKind Kind { get; }
    internal CharacterLanguageStyleStageFBAdmission Admission { get; }
    internal string? CorrelationId { get; }
    internal Guid? AuthorizationId { get; }
    internal string? ProviderRouteId { get; }
    internal string? PromptBaselineSha256 { get; }
    internal int PromptBaselineCharacters { get; }
    internal ExistingCharacterPromptInfluenceMode? ExistingMode { get; }
    internal CharacterLanguageStyleStageFBExclusion Exclusions { get; }
    internal LanguageStylePromptOwner? FutureOwnerObservation { get; }
    internal DateTimeOffset ObservedAt { get; }

    internal CharacterLanguageStyleStageFBRecord(
        long sequence,
        CharacterLanguageStyleStageFBRecordKind kind,
        CharacterLanguageStyleStageFBAdmission admission,
        string? correlationId,
        Guid? authorizationId,
        string? providerRouteId,
        string? promptBaselineSha256,
        int promptBaselineCharacters,
        ExistingCharacterPromptInfluenceMode? existingMode,
        CharacterLanguageStyleStageFBExclusion exclusions,
        LanguageStylePromptOwner? futureOwnerObservation,
        DateTimeOffset observedAt)
    {
        Sequence =
            CharacterLanguageStyleStageFContractGuard.Positive(
                sequence,
                nameof(sequence));
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFBRecordKind),
                kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFBAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        Kind = kind;
        Admission = admission;
        CorrelationId = correlationId;
        AuthorizationId = authorizationId;
        ProviderRouteId = providerRouteId;
        PromptBaselineSha256 = promptBaselineSha256;
        PromptBaselineCharacters = promptBaselineCharacters;
        ExistingMode = existingMode;
        Exclusions = exclusions;
        FutureOwnerObservation = futureOwnerObservation;
        ObservedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                observedAt,
                nameof(observedAt));
    }
}

internal sealed class CharacterLanguageStyleStageFBAuthorityCounters
{
    internal static CharacterLanguageStyleStageFBAuthorityCounters Zero {
        get;
    } = new();

    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal int OwnerLeasesIssued => 0;
    internal int DirectivesCreated => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int DecisionOutputs => 0;
    internal int StateWrites => 0;
    internal int PersistenceWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int VoiceOutputs => 0;
    internal int ExecutionOutputs => 0;
    internal bool IsZero =>
        PromptWrites == 0
        && ReplyOutputs == 0
        && ProviderCalls == 0
        && ProviderRetries == 0
        && OwnerLeasesIssued == 0
        && DirectivesCreated == 0
        && MemoryCalls == 0
        && EvidenceCalls == 0
        && ReasoningCalls == 0
        && DecisionOutputs == 0
        && StateWrites == 0
        && PersistenceWrites == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && VoiceOutputs == 0
        && ExecutionOutputs == 0;

    private CharacterLanguageStyleStageFBAuthorityCounters()
    {
    }
}

internal sealed class CharacterLanguageStyleStageFBSnapshot
{
    internal CharacterLanguageStyleStageFBStatus Status { get; }
    internal int PendingCount { get; }
    internal int TerminalCount { get; }
    internal IReadOnlyList<CharacterLanguageStyleStageFBRecord> Records {
        get;
    }
    internal CharacterLanguageStyleStageFBAuthorityCounters
        AuthorityCounters { get; }
    internal bool IsEmpty =>
        PendingCount == 0
        && TerminalCount == 0
        && Records.Count == 0;

    internal CharacterLanguageStyleStageFBSnapshot(
        CharacterLanguageStyleStageFBStatus status,
        int pendingCount,
        int terminalCount,
        IEnumerable<CharacterLanguageStyleStageFBRecord> records)
    {
        Status = status;
        PendingCount = pendingCount;
        TerminalCount = terminalCount;
        Records = (records
                ?? throw new ArgumentNullException(nameof(records)))
            .ToArray();
        AuthorityCounters =
            CharacterLanguageStyleStageFBAuthorityCounters.Zero;
    }
}

internal sealed class CharacterLanguageStyleStageFBObserveHost
{
    private sealed class CorrelationState
    {
        internal CharacterLanguageStyleStageFBTurnCorrelation Correlation {
            get;
        }
        internal bool Terminal { get; set; }

        internal CorrelationState(
            CharacterLanguageStyleStageFBTurnCorrelation correlation)
        {
            Correlation = correlation;
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, CorrelationState> _correlations =
        new(StringComparer.Ordinal);
    private readonly List<CharacterLanguageStyleStageFBRecord> _records =
        new();
    private CharacterLanguageStyleStageFBConfiguration _configuration =
        CharacterLanguageStyleStageFBConfiguration.Off;
    private CharacterLanguageStyleStageFBStatus _status =
        CharacterLanguageStyleStageFBStatus.Off;
    private long _recordSequence;
    private int _terminalCount;

    internal bool IsObserving
    {
        get
        {
            lock (_gate)
            {
                return _status
                    == CharacterLanguageStyleStageFBStatus.Observe;
            }
        }
    }

    internal void Reconcile(
        CharacterLanguageStyleStageFBConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            if (!configuration.Enabled)
            {
                ClearLocked();
                _configuration = configuration;
                _status = CharacterLanguageStyleStageFBStatus.Off;
                return;
            }
            _configuration = configuration;
            if (_status is CharacterLanguageStyleStageFBStatus.Off
                or CharacterLanguageStyleStageFBStatus.Capacity)
            {
                _status = CharacterLanguageStyleStageFBStatus.Observe;
            }
        }
    }

    internal CharacterLanguageStyleStageFBTurnCorrelation? TryBegin(
        string correlationId,
        long sourceSequence,
        PlayerTriggeredRespondAuthorization authorization,
        DateTimeOffset observedAt)
    {
        lock (_gate)
        {
            if (_status != CharacterLanguageStyleStageFBStatus.Observe)
                return null;
            if (_correlations.ContainsKey(correlationId))
            {
                AddRecordLocked(
                    CharacterLanguageStyleStageFBRecordKind.Turn,
                    CharacterLanguageStyleStageFBAdmission
                        .DuplicateCorrelationRejected,
                    correlationId,
                    authorization.AuthorizationId,
                    observedAt);
                return null;
            }
            if (_records.Count + 2 > _configuration.Capacity)
            {
                _status = CharacterLanguageStyleStageFBStatus.Capacity;
                return null;
            }

            var correlation =
                new CharacterLanguageStyleStageFBTurnCorrelation(
                    correlationId,
                    sourceSequence,
                    authorization,
                    observedAt);
            _correlations.Add(
                correlation.CorrelationId,
                new CorrelationState(correlation));
            AddRecordLocked(
                CharacterLanguageStyleStageFBRecordKind.Turn,
                CharacterLanguageStyleStageFBAdmission.TurnObserved,
                correlation.CorrelationId,
                authorization.AuthorizationId,
                observedAt);
            return correlation;
        }
    }

    internal CharacterLanguageStyleStageFBRequestObservation?
        TryObserveRequest(
            CharacterLanguageStyleStageFBTurnCorrelation correlation,
            CharacterRuntimeIdentity activeRuntimeIdentity,
            string activeSessionId,
            Guid activeSituationId,
            long activeSituationRevision,
            string providerRouteId,
            string promptBaselineSha256,
            int promptBaselineCharacters,
            ExistingCharacterPromptInfluenceMode existingMode,
            CharacterLanguageStyleStageFBExclusion exclusions,
            DateTimeOffset observedAt)
    {
        if (correlation == null)
            throw new ArgumentNullException(nameof(correlation));
        lock (_gate)
        {
            if (_status != CharacterLanguageStyleStageFBStatus.Observe)
                return null;
            if (!_correlations.TryGetValue(
                    correlation.CorrelationId,
                    out var state))
            {
                AddRecordLocked(
                    CharacterLanguageStyleStageFBRecordKind.Request,
                    CharacterLanguageStyleStageFBAdmission
                        .CorrelationNotFound,
                    correlation.CorrelationId,
                    correlation.Authorization.AuthorizationId,
                    observedAt);
                return null;
            }
            if (state.Terminal)
            {
                AddRecordLocked(
                    CharacterLanguageStyleStageFBRecordKind.Request,
                    CharacterLanguageStyleStageFBAdmission
                        .CorrelationAlreadyTerminal,
                    correlation.CorrelationId,
                    correlation.Authorization.AuthorizationId,
                    observedAt);
                return null;
            }

            state.Terminal = true;
            _terminalCount++;
            var admission = Validate(
                state.Correlation,
                activeRuntimeIdentity,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                providerRouteId,
                promptBaselineSha256,
                promptBaselineCharacters,
                existingMode,
                exclusions,
                observedAt);
            var futureOwner =
                admission
                    == CharacterLanguageStyleStageFBAdmission
                        .RequestEligible
                    ? LanguageStylePromptOwner.CharacterRuntimeOwned
                    : LanguageStylePromptOwner.LegacyOwned;
            var observation =
                admission
                    is CharacterLanguageStyleStageFBAdmission
                        .RequestEligible
                    or CharacterLanguageStyleStageFBAdmission
                        .LegacyAssistConflict
                    or CharacterLanguageStyleStageFBAdmission
                        .FirstLiveExcluded
                ? new CharacterLanguageStyleStageFBRequestObservation(
                    state.Correlation,
                    providerRouteId,
                    promptBaselineSha256,
                    promptBaselineCharacters,
                    existingMode,
                    exclusions,
                    admission,
                    futureOwner,
                    observedAt)
                : null;
            AddRecordLocked(
                CharacterLanguageStyleStageFBRecordKind.Request,
                admission,
                correlation.CorrelationId,
                correlation.Authorization.AuthorizationId,
                observedAt,
                providerRouteId,
                promptBaselineSha256,
                promptBaselineCharacters,
                existingMode,
                exclusions,
                futureOwner);
            return observation;
        }
    }

    internal CharacterLanguageStyleStageFBSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new CharacterLanguageStyleStageFBSnapshot(
                _status,
                _correlations.Values.Count(state => !state.Terminal),
                _terminalCount,
                _records);
        }
    }

    internal void Restart()
    {
        lock (_gate)
        {
            ClearLocked();
            _configuration =
                CharacterLanguageStyleStageFBConfiguration.Off;
            _status = CharacterLanguageStyleStageFBStatus.Off;
        }
    }

    internal void StopByFault()
    {
        lock (_gate)
        {
            ClearLocked();
            _configuration =
                CharacterLanguageStyleStageFBConfiguration.Off;
            _status = CharacterLanguageStyleStageFBStatus.Fault;
        }
    }

    private static CharacterLanguageStyleStageFBAdmission Validate(
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string promptBaselineSha256,
        int promptBaselineCharacters,
        ExistingCharacterPromptInfluenceMode existingMode,
        CharacterLanguageStyleStageFBExclusion exclusions,
        DateTimeOffset observedAt)
    {
        if (observedAt >= correlation.ValidUntil)
        {
            return CharacterLanguageStyleStageFBAdmission
                .AuthorizationExpired;
        }
        if (!correlation.Authorization.RuntimeIdentity.Matches(
                activeRuntimeIdentity))
        {
            return CharacterLanguageStyleStageFBAdmission.RuntimeMismatch;
        }
        if (!string.Equals(
                correlation.Authorization.SessionId,
                activeSessionId,
                StringComparison.Ordinal))
        {
            return CharacterLanguageStyleStageFBAdmission.SessionMismatch;
        }
        if (correlation.Authorization.SituationId != activeSituationId
            || correlation.Authorization.SituationRevision
                != activeSituationRevision)
        {
            return CharacterLanguageStyleStageFBAdmission
                .SituationMismatch;
        }
        if (string.IsNullOrWhiteSpace(providerRouteId))
        {
            return CharacterLanguageStyleStageFBAdmission
                .ProviderRouteInvalid;
        }
        if (promptBaselineCharacters <= 0
            || string.IsNullOrWhiteSpace(promptBaselineSha256)
            || promptBaselineSha256.Length != 64
            || promptBaselineSha256.Any(
                character => !Uri.IsHexDigit(character)))
        {
            return CharacterLanguageStyleStageFBAdmission
                .PromptMetadataInvalid;
        }
        if (existingMode == ExistingCharacterPromptInfluenceMode.Assist)
        {
            return CharacterLanguageStyleStageFBAdmission
                .LegacyAssistConflict;
        }
        if (exclusions != CharacterLanguageStyleStageFBExclusion.None)
        {
            return CharacterLanguageStyleStageFBAdmission
                .FirstLiveExcluded;
        }
        return CharacterLanguageStyleStageFBAdmission.RequestEligible;
    }

    private void AddRecordLocked(
        CharacterLanguageStyleStageFBRecordKind kind,
        CharacterLanguageStyleStageFBAdmission admission,
        string? correlationId,
        Guid? authorizationId,
        DateTimeOffset observedAt,
        string? providerRouteId = null,
        string? promptBaselineSha256 = null,
        int promptBaselineCharacters = 0,
        ExistingCharacterPromptInfluenceMode? existingMode = null,
        CharacterLanguageStyleStageFBExclusion exclusions =
            CharacterLanguageStyleStageFBExclusion.None,
        LanguageStylePromptOwner? futureOwnerObservation = null)
    {
        if (_records.Count >= _configuration.Capacity)
        {
            _status = CharacterLanguageStyleStageFBStatus.Capacity;
            return;
        }
        _records.Add(
            new CharacterLanguageStyleStageFBRecord(
                ++_recordSequence,
                kind,
                admission,
                correlationId,
                authorizationId,
                providerRouteId,
                promptBaselineSha256,
                promptBaselineCharacters,
                existingMode,
                exclusions,
                futureOwnerObservation,
                observedAt));
    }

    private void ClearLocked()
    {
        _correlations.Clear();
        _records.Clear();
        _recordSequence = 0;
        _terminalCount = 0;
    }
}
