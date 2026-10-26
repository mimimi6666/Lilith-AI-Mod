using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum D022SliceDObserveStatus
{
    Off = 0,
    Observe = 1,
    Fault = 2,
    Capacity = 3
}

internal enum D022SliceDObserveAdmission
{
    NotRunning = 0,
    Observed = 1,
    EligiblePreviewRecorded = 2,
    CapacityReached = 3,
    Faulted = 4
}

internal sealed class D022SliceDObserveRecord
{
    internal string ObservationId { get; }
    internal long Sequence { get; }
    internal MotionDirectorDisposition Disposition { get; }
    internal MotionIntentKind Kind { get; }
    internal MotionIntentSource Source { get; }
    internal MotionDirectorReason Reason { get; }
    internal string ProviderId { get; }
    internal bool EligiblePlayOneShot { get; }
    internal D022OpportunityOwnership? ExistingOwner { get; }
    internal bool WouldRequireAuthorization => EligiblePlayOneShot;
    internal bool Executable => false;
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long ConfigurationGeneration { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal DateTimeOffset ObservedAt { get; }

    internal D022SliceDObserveRecord(
        string observationId,
        long sequence,
        MotionDirectorProposal proposal,
        bool eligiblePlayOneShot,
        D022OpportunityOwnership? existingOwner,
        CharacterRuntimeIdentity runtimeIdentity,
        long configurationGeneration,
        string sessionId,
        string epochId,
        DateTimeOffset observedAt)
    {
        ObservationId = D022ContractGuard.Identifier(
            observationId,
            nameof(observationId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (existingOwner.HasValue
            && !Enum.IsDefined(
                typeof(D022OpportunityOwnership),
                existingOwner.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(existingOwner));
        }
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }

        Sequence = sequence;
        Disposition = proposal.Disposition;
        Kind = proposal.Kind;
        Source = proposal.Source;
        Reason = proposal.Reason;
        ProviderId = D022ContractGuard.Identifier(
            proposal.ProviderId,
            nameof(proposal));
        EligiblePlayOneShot = eligiblePlayOneShot;
        ExistingOwner = existingOwner;
        ConfigurationGeneration = configurationGeneration;
        SessionId = D022ContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        EpochId = D022ContractGuard.Identifier(
            epochId,
            nameof(epochId));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
    }
}

internal sealed class D022SliceDObserveSnapshot
{
    internal D022SliceDObserveStatus Status { get; }
    internal bool Enabled { get; }
    internal bool SourceEnabled { get; }
    internal long ConfigurationGeneration { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal int ObservedCount { get; }
    internal int EligiblePreviewCount { get; }
    internal int NonEligibleCount { get; }
    internal int RetiredCount { get; }
    internal int RecordCount { get; }
    internal D022SliceDObserveRecord? Latest { get; }
    internal int TicketIssues => 0;
    internal int TicketConsumptions => 0;
    internal int QueueWrites => 0;
    internal int MotionExecutions => 0;
    internal int AuthorityOutputs => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;

    internal D022SliceDObserveSnapshot(
        D022SliceDObserveStatus status,
        bool enabled,
        bool sourceEnabled,
        long configurationGeneration,
        CharacterRuntimeIdentity? runtimeIdentity,
        string sessionId,
        string epochId,
        int observedCount,
        int eligiblePreviewCount,
        int nonEligibleCount,
        int retiredCount,
        int recordCount,
        D022SliceDObserveRecord? latest)
    {
        if (!Enum.IsDefined(typeof(D022SliceDObserveStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (configurationGeneration < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        if (observedCount < 0
            || eligiblePreviewCount < 0
            || nonEligibleCount < 0
            || retiredCount < 0
            || recordCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedCount));
        }
        if (eligiblePreviewCount + nonEligibleCount != observedCount)
        {
            throw new ArgumentException(
                "Observe counters must account for every observation.");
        }
        if (recordCount > D022SliceDObserveRuntimeHost.MaximumRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        }

        Status = status;
        Enabled = enabled;
        SourceEnabled = sourceEnabled;
        ConfigurationGeneration = configurationGeneration;
        RuntimeIdentity = runtimeIdentity;
        SessionId = sessionId ?? string.Empty;
        EpochId = epochId ?? string.Empty;
        ObservedCount = observedCount;
        EligiblePreviewCount = eligiblePreviewCount;
        NonEligibleCount = nonEligibleCount;
        RetiredCount = retiredCount;
        RecordCount = recordCount;
        Latest = latest;
    }

    internal static D022SliceDObserveSnapshot Empty { get; } =
        new(
            D022SliceDObserveStatus.Off,
            enabled: false,
            sourceEnabled: false,
            configurationGeneration: 0,
            runtimeIdentity: null,
            sessionId: string.Empty,
            epochId: string.Empty,
            observedCount: 0,
            eligiblePreviewCount: 0,
            nonEligibleCount: 0,
            retiredCount: 0,
            recordCount: 0,
            latest: null);
}

internal sealed class D022SliceDObserveRuntimeHost
{
    internal const int MaximumRecords = 128;

    private readonly object _gate = new();
    private readonly List<D022SliceDObserveRecord> _records = new();
    private D022SliceDObserveStatus _status =
        D022SliceDObserveStatus.Off;
    private bool _enabled;
    private bool _sourceEnabled;
    private long _configurationGeneration;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private string _sessionId = string.Empty;
    private string _epochId = string.Empty;
    private long _nextObservationSequence = 1;
    private int _observedCount;
    private int _eligiblePreviewCount;
    private int _nonEligibleCount;
    private int _retiredCount;

    internal void Reconcile(
        bool enabled,
        bool sourceEnabled,
        string runtimeInstanceId,
        long runtimeGeneration,
        string sessionId,
        string epochId,
        DateTimeOffset occurredAt)
    {
        if (!enabled)
        {
            lock (_gate)
            {
                if (_status != D022SliceDObserveStatus.Off
                    || _enabled
                    || _records.Count > 0)
                {
                    ResetToOff();
                }
            }
            return;
        }

        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        var normalizedRuntimeInstance =
            D022ContractGuard.Identifier(
                runtimeInstanceId,
                nameof(runtimeInstanceId));
        var normalizedSession = D022ContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        var normalizedEpoch = D022ContractGuard.Identifier(
            epochId,
            nameof(epochId));
        if (runtimeGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeGeneration));
        }

        lock (_gate)
        {
            var contextMatches = _runtimeIdentity != null
                && string.Equals(
                    _runtimeIdentity.RuntimeInstanceId,
                    normalizedRuntimeInstance,
                    StringComparison.Ordinal)
                && _runtimeIdentity.Generation == runtimeGeneration
                && string.Equals(
                    _sessionId,
                    normalizedSession,
                    StringComparison.Ordinal)
                && string.Equals(
                    _epochId,
                    normalizedEpoch,
                    StringComparison.Ordinal);
            if (_enabled && contextMatches
                && _status != D022SliceDObserveStatus.Fault)
            {
                if (_sourceEnabled && !sourceEnabled)
                {
                    _retiredCount += _records.Count;
                    _records.Clear();
                    _nextObservationSequence = 1;
                    _observedCount = 0;
                    _eligiblePreviewCount = 0;
                    _nonEligibleCount = 0;
                    _status = D022SliceDObserveStatus.Observe;
                }
                _sourceEnabled = sourceEnabled;
                return;
            }

            if (_enabled)
                _retiredCount += _records.Count;
            StartObserve(
                sourceEnabled,
                normalizedRuntimeInstance,
                runtimeGeneration,
                normalizedSession,
                normalizedEpoch,
                normalizedAt);
        }
    }

    internal D022SliceDObserveAdmission Observe(
        MotionDirectorProposal proposal,
        D022SliceBGateResult ownership,
        DateTimeOffset observedAt)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (ownership == null)
            throw new ArgumentNullException(nameof(ownership));

        lock (_gate)
        {
            if (!_enabled || _status == D022SliceDObserveStatus.Off)
                return D022SliceDObserveAdmission.NotRunning;
            if (_status == D022SliceDObserveStatus.Fault)
                return D022SliceDObserveAdmission.Faulted;
            if (_status == D022SliceDObserveStatus.Capacity
                || _records.Count >= MaximumRecords)
            {
                _status = D022SliceDObserveStatus.Capacity;
                return D022SliceDObserveAdmission.CapacityReached;
            }

            var normalizedAt = DecisionCoreContractGuard.Timestamp(
                observedAt,
                nameof(observedAt));
            var eligible =
                D022SliceBGateResult.IsAllowlistedOccurrence(proposal);
            var sequence = _nextObservationSequence++;
            var record = new D022SliceDObserveRecord(
                ObservationId(sequence),
                sequence,
                proposal,
                eligible,
                ownership.Ownership,
                _runtimeIdentity!,
                _configurationGeneration,
                _sessionId,
                _epochId,
                normalizedAt);
            _records.Add(record);
            _observedCount++;
            if (eligible)
                _eligiblePreviewCount++;
            else
                _nonEligibleCount++;
            if (_records.Count >= MaximumRecords)
                _status = D022SliceDObserveStatus.Capacity;

            return eligible
                ? D022SliceDObserveAdmission.EligiblePreviewRecorded
                : D022SliceDObserveAdmission.Observed;
        }
    }

    internal void StopByFault(DateTimeOffset occurredAt)
    {
        _ = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            _retiredCount += _records.Count;
            _records.Clear();
            _status = D022SliceDObserveStatus.Fault;
            _enabled = false;
            _sourceEnabled = false;
        }
    }

    internal D022SliceDObserveSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_status == D022SliceDObserveStatus.Off
                && !_enabled
                && _records.Count == 0
                && _configurationGeneration == 0)
            {
                return D022SliceDObserveSnapshot.Empty;
            }
            return new D022SliceDObserveSnapshot(
                _status,
                _enabled,
                _sourceEnabled,
                _configurationGeneration,
                _runtimeIdentity,
                _sessionId,
                _epochId,
                _observedCount,
                _eligiblePreviewCount,
                _nonEligibleCount,
                _retiredCount,
                _records.Count,
                _records.Count == 0 ? null : _records[^1]);
        }
    }

    private void StartObserve(
        bool sourceEnabled,
        string runtimeInstanceId,
        long runtimeGeneration,
        string sessionId,
        string epochId,
        DateTimeOffset occurredAt)
    {
        _ = occurredAt;
        _configurationGeneration++;
        _runtimeIdentity = new CharacterRuntimeIdentity(
            "character-runtime-profile-v1",
            runtimeInstanceId,
            runtimeGeneration,
            sessionId,
            "local-owner");
        _sessionId = sessionId;
        _epochId = epochId;
        _sourceEnabled = sourceEnabled;
        _enabled = true;
        _status = D022SliceDObserveStatus.Observe;
        _records.Clear();
        _nextObservationSequence = 1;
        _observedCount = 0;
        _eligiblePreviewCount = 0;
        _nonEligibleCount = 0;
    }

    private void ResetToOff()
    {
        _records.Clear();
        _status = D022SliceDObserveStatus.Off;
        _enabled = false;
        _sourceEnabled = false;
        _runtimeIdentity = null;
        _sessionId = string.Empty;
        _epochId = string.Empty;
        _nextObservationSequence = 1;
        _observedCount = 0;
        _eligiblePreviewCount = 0;
        _nonEligibleCount = 0;
        _retiredCount = 0;
        _configurationGeneration = 0;
    }

    private string ObservationId(long sequence)
        => _runtimeIdentity!.RuntimeInstanceId
            + "-"
            + _runtimeIdentity.Generation
            + "-"
            + _configurationGeneration
            + "-"
            + sequence;
}
