using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal enum LivingEventKind
{
    UserTurnObserved = 0,
    ModelTurnCompleted = 1,
    CapabilityCompleted = 2,
    ExplicitIdentityCorrection = 3,
    ExplicitPreferenceCorrection = 4,
    ExplicitWarmth = 5,
    ExplicitCare = 6,
    ExplicitPraise = 7,
    ExplicitApology = 8,
    ExplicitBoundaryPressure = 9,
    ExplicitHarshCommand = 10,
    ExplicitInsult = 11,
    ExplicitWakeRequest = 12,
    DirectPlayerDrag = 13,
    AiNoteDue = 14,
    SessionOpened = 15,
    SessionClosed = 16
}

internal enum LivingPrivacyClass
{
    MetadataOnly = 0,
    LocalPersonal = 1,
    LocalSensitive = 2,
    ExternallyTransmittable = 3
}

internal enum ContinuityOwnershipDomain
{
    PromptAssembly = 0,
    CharacterStateWrite = 1,
    ProactiveOccurrence = 2
}

internal enum ContinuityOwner
{
    LegacyOwned = 0,
    LivingContinuityOwned = 1
}

internal enum ContinuityOwnershipDisposition
{
    Acquired = 0,
    AlreadyOwned = 1,
    Conflict = 2,
    Expired = 3,
    StaleRuntime = 4,
    CrossSession = 5,
    DuplicateConsumption = 6,
    Retired = 7,
    NotFound = 8,
    CapacityReached = 9
}

internal enum ContinuityRetirementReason
{
    Consumed = 0,
    Cancelled = 1,
    Expired = 2,
    SituationRevisionChanged = 3,
    RuntimeGenerationChanged = 4,
    SessionClosed = 5,
    SourceDisabled = 6,
    MasterDisabled = 7,
    Faulted = 8
}

internal static class LivingContinuityContractGuard
{
    internal const int MaximumIdentifierLength = 128;

    internal static string Identifier(
        string? value,
        string parameterName)
        => DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);

    internal static DateTimeOffset Timestamp(
        DateTimeOffset value,
        string parameterName)
        => DecisionCoreContractGuard.Timestamp(value, parameterName);

    internal static string Sha256(
        string? value,
        string parameterName)
    {
        var normalized = Identifier(value, parameterName);
        if (normalized.Length != 64)
            throw new ArgumentException(
                "A SHA-256 digest must contain exactly 64 hexadecimal characters.",
                parameterName);
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (!Uri.IsHexDigit(character))
                throw new ArgumentException(
                    "A SHA-256 digest must contain only hexadecimal characters.",
                    parameterName);
        }
        return normalized.ToUpperInvariant();
    }
}

internal sealed class LivingEventMetadata
{
    internal string EventId { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string SourceId { get; }
    internal long SourceRevision { get; }
    internal LivingEventKind Kind { get; }
    internal LivingPrivacyClass PrivacyClass { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal string ContentHash { get; }

    internal LivingEventMetadata(
        string eventId,
        string sessionId,
        Guid situationId,
        long situationRevision,
        CharacterRuntimeIdentity runtimeIdentity,
        string sourceId,
        long sourceRevision,
        LivingEventKind kind,
        LivingPrivacyClass privacyClass,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        string contentHash)
    {
        EventId = LivingContinuityContractGuard.Identifier(
            eventId,
            nameof(eventId));
        SessionId = LivingContinuityContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        if (situationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(situationId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!string.Equals(
                SessionId,
                runtimeIdentity.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Event and runtime must belong to the same session.",
                nameof(sessionId));
        }
        SourceId = LivingContinuityContractGuard.Identifier(
            sourceId,
            nameof(sourceId));
        if (sourceRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        if (!Enum.IsDefined(typeof(LivingEventKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(LivingPrivacyClass), privacyClass))
            throw new ArgumentOutOfRangeException(nameof(privacyClass));
        OccurredAt = LivingContinuityContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        ObservedAt = LivingContinuityContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (OccurredAt > ObservedAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        ContentHash = LivingContinuityContractGuard.Sha256(
            contentHash,
            nameof(contentHash));
        SituationId = situationId;
        SituationRevision = situationRevision;
        SourceRevision = sourceRevision;
        Kind = kind;
        PrivacyClass = privacyClass;
    }
}

internal sealed class ContinuityOwnershipRequest
{
    internal string LeaseId { get; }
    internal ContinuityOwnershipDomain Domain { get; }
    internal ContinuityOwner RequestedOwner { get; }
    internal string OccurrenceId { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal ContinuityOwnershipRequest(
        string leaseId,
        ContinuityOwnershipDomain domain,
        ContinuityOwner requestedOwner,
        string occurrenceId,
        string sessionId,
        Guid situationId,
        long situationRevision,
        CharacterRuntimeIdentity runtimeIdentity,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        LeaseId = LivingContinuityContractGuard.Identifier(
            leaseId,
            nameof(leaseId));
        if (!Enum.IsDefined(typeof(ContinuityOwnershipDomain), domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (!Enum.IsDefined(typeof(ContinuityOwner), requestedOwner))
            throw new ArgumentOutOfRangeException(nameof(requestedOwner));
        OccurrenceId = LivingContinuityContractGuard.Identifier(
            occurrenceId,
            nameof(occurrenceId));
        SessionId = LivingContinuityContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        if (situationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(situationId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!string.Equals(
                SessionId,
                runtimeIdentity.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Lease and runtime must belong to the same session.",
                nameof(sessionId));
        }
        IssuedAt = LivingContinuityContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ExpiresAt = LivingContinuityContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= IssuedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Domain = domain;
        RequestedOwner = requestedOwner;
        SituationId = situationId;
        SituationRevision = situationRevision;
    }
}

internal sealed class ContinuityOwnershipRecord
{
    internal ContinuityOwnershipRequest Request { get; }
    internal ContinuityOwnershipDisposition Disposition { get; }
    internal bool IsTerminal { get; }
    internal bool WasConsumed { get; }
    internal ContinuityRetirementReason? RetirementReason { get; }
    internal DateTimeOffset RecordedAt { get; }

    internal ContinuityOwnershipRecord(
        ContinuityOwnershipRequest request,
        ContinuityOwnershipDisposition disposition,
        bool isTerminal,
        bool wasConsumed,
        ContinuityRetirementReason? retirementReason,
        DateTimeOffset recordedAt)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        if (!Enum.IsDefined(typeof(ContinuityOwnershipDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (retirementReason.HasValue
            && !Enum.IsDefined(
                typeof(ContinuityRetirementReason),
                retirementReason.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(retirementReason));
        }
        if (wasConsumed
            && retirementReason != ContinuityRetirementReason.Consumed)
        {
            throw new ArgumentException(
                "Consumed ownership must have the Consumed retirement reason.",
                nameof(retirementReason));
        }
        if (!isTerminal && retirementReason.HasValue)
            throw new ArgumentException(
                "Only terminal ownership may carry a retirement reason.",
                nameof(retirementReason));
        RecordedAt = LivingContinuityContractGuard.Timestamp(
            recordedAt,
            nameof(recordedAt));
        if (RecordedAt < request.IssuedAt)
            throw new ArgumentOutOfRangeException(nameof(recordedAt));
        Disposition = disposition;
        IsTerminal = isTerminal;
        WasConsumed = wasConsumed;
        RetirementReason = retirementReason;
    }
}

internal readonly struct LivingContinuityAuthorityCounters
{
    internal int DecisionWrites { get; }
    internal int PromptWrites { get; }
    internal int ProviderCalls { get; }
    internal int CharacterWrites { get; }
    internal int RelationshipWrites { get; }
    internal int QueueWrites { get; }
    internal int MotionExecutions { get; }
    internal int CapabilityExecutions { get; }

    internal LivingContinuityAuthorityCounters(
        int decisionWrites,
        int promptWrites,
        int providerCalls,
        int characterWrites,
        int relationshipWrites,
        int queueWrites,
        int motionExecutions,
        int capabilityExecutions)
    {
        if (decisionWrites < 0
            || promptWrites < 0
            || providerCalls < 0
            || characterWrites < 0
            || relationshipWrites < 0
            || queueWrites < 0
            || motionExecutions < 0
            || capabilityExecutions < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decisionWrites),
                "Authority counters cannot be negative.");
        }
        DecisionWrites = decisionWrites;
        PromptWrites = promptWrites;
        ProviderCalls = providerCalls;
        CharacterWrites = characterWrites;
        RelationshipWrites = relationshipWrites;
        QueueWrites = queueWrites;
        MotionExecutions = motionExecutions;
        CapabilityExecutions = capabilityExecutions;
    }

    internal bool IsZero =>
        DecisionWrites == 0
        && PromptWrites == 0
        && ProviderCalls == 0
        && CharacterWrites == 0
        && RelationshipWrites == 0
        && QueueWrites == 0
        && MotionExecutions == 0
        && CapabilityExecutions == 0;

    internal static LivingContinuityAuthorityCounters Zero => default;
}

internal sealed class ContinuityOwnershipRegistry
{
    internal const int MaximumActiveRecords = 64;
    internal const int MaximumTrackedRecords = 576;

    private readonly Dictionary<string, ContinuityOwnershipRecord> _records =
        new(StringComparer.Ordinal);

    internal int Count
    {
        get
        {
            lock (_records)
                return _records.Count;
        }
    }
    internal LivingContinuityAuthorityCounters AuthorityCounters =>
        LivingContinuityAuthorityCounters.Zero;

    internal ContinuityOwnershipRecord Acquire(
        ContinuityOwnershipRequest request,
        CharacterRuntimeIdentity activeRuntime,
        DateTimeOffset now)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));

        lock (_records)
        {
            RemoveExpiredTerminalRecords(now);
            var key = Key(request.Domain, request.OccurrenceId);
            if (_records.TryGetValue(key, out var existing))
            {
                if (existing.IsTerminal)
                {
                    return Snapshot(
                        request,
                        ContinuityOwnershipDisposition.Retired,
                        true,
                        false,
                        existing.RetirementReason,
                        now);
                }
                return Snapshot(
                    request,
                    existing.Request.RequestedOwner == request.RequestedOwner
                        ? ContinuityOwnershipDisposition.AlreadyOwned
                        : ContinuityOwnershipDisposition.Conflict,
                    false,
                    false,
                    null,
                    now);
            }

            if (now >= request.ExpiresAt)
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.Expired,
                    true,
                    false,
                    ContinuityRetirementReason.Expired,
                    now);
            }
            if (!request.RuntimeIdentity.Matches(activeRuntime))
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.StaleRuntime,
                    true,
                    false,
                    ContinuityRetirementReason.RuntimeGenerationChanged,
                    now);
            }
            if (!string.Equals(
                    request.SessionId,
                    activeRuntime.OrchestratorSessionId,
                    StringComparison.Ordinal))
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.CrossSession,
                    true,
                    false,
                    ContinuityRetirementReason.SessionClosed,
                    now);
            }
            if (_records.Count >= MaximumTrackedRecords
                || _records.Values.Count(record => !record.IsTerminal)
                    >= MaximumActiveRecords)
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.CapacityReached,
                    true,
                    false,
                    ContinuityRetirementReason.Faulted,
                    now);
            }

            var acquired = Snapshot(
                request,
                ContinuityOwnershipDisposition.Acquired,
                false,
                false,
                null,
                now);
            _records.Add(key, acquired);
            return acquired;
        }
    }

    internal ContinuityOwnershipRecord Consume(
        ContinuityOwnershipRequest request,
        CharacterRuntimeIdentity activeRuntime,
        Guid activeSituationId,
        long activeSituationRevision,
        DateTimeOffset now)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        if (activeSituationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(activeSituationId));
        if (activeSituationRevision <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(activeSituationRevision));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));

        lock (_records)
        {
            var key = Key(request.Domain, request.OccurrenceId);
            if (!_records.TryGetValue(key, out var existing))
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.NotFound,
                    true,
                    false,
                    ContinuityRetirementReason.Faulted,
                    now);
            }
            if (existing.IsTerminal)
            {
                return Snapshot(
                    request,
                    existing.WasConsumed
                        ? ContinuityOwnershipDisposition
                            .DuplicateConsumption
                        : ContinuityOwnershipDisposition.Retired,
                    true,
                    existing.WasConsumed,
                    existing.RetirementReason,
                    now);
            }
            if (!string.Equals(
                    existing.Request.LeaseId,
                    request.LeaseId,
                    StringComparison.Ordinal)
                || existing.Request.RequestedOwner
                    != request.RequestedOwner)
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.Conflict,
                    true,
                    false,
                    ContinuityRetirementReason.Faulted,
                    now);
            }
            if (now >= request.ExpiresAt)
            {
                var expired = Snapshot(
                    request,
                    ContinuityOwnershipDisposition.Expired,
                    true,
                    false,
                    ContinuityRetirementReason.Expired,
                    now);
                _records[key] = expired;
                return expired;
            }
            if (!request.RuntimeIdentity.Matches(activeRuntime))
            {
                var stale = Snapshot(
                    request,
                    ContinuityOwnershipDisposition.StaleRuntime,
                    true,
                    false,
                    ContinuityRetirementReason
                        .RuntimeGenerationChanged,
                    now);
                _records[key] = stale;
                return stale;
            }
            if (request.SituationId != activeSituationId
                || request.SituationRevision != activeSituationRevision)
            {
                var retired = Snapshot(
                    request,
                    ContinuityOwnershipDisposition.Retired,
                    true,
                    false,
                    ContinuityRetirementReason
                        .SituationRevisionChanged,
                    now);
                _records[key] = retired;
                return retired;
            }

            var consumed = Snapshot(
                request,
                ContinuityOwnershipDisposition.Acquired,
                true,
                true,
                ContinuityRetirementReason.Consumed,
                now);
            _records[key] = consumed;
            return consumed;
        }
    }

    internal ContinuityOwnershipRecord Retire(
        ContinuityOwnershipRequest request,
        ContinuityRetirementReason reason,
        DateTimeOffset now)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (!Enum.IsDefined(typeof(ContinuityRetirementReason), reason)
            || reason == ContinuityRetirementReason.Consumed)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        lock (_records)
        {
            var key = Key(request.Domain, request.OccurrenceId);
            if (!_records.TryGetValue(key, out var existing))
            {
                return Snapshot(
                    request,
                    ContinuityOwnershipDisposition.NotFound,
                    true,
                    false,
                    reason,
                    now);
            }
            if (existing.IsTerminal)
                return existing;
            var retired = Snapshot(
                existing.Request,
                ContinuityOwnershipDisposition.Retired,
                true,
                false,
                reason,
                now);
            _records[key] = retired;
            return retired;
        }
    }

    internal ContinuityOwnershipRecord? Find(
        ContinuityOwnershipDomain domain,
        string occurrenceId)
    {
        occurrenceId = LivingContinuityContractGuard.Identifier(
            occurrenceId,
            nameof(occurrenceId));
        lock (_records)
        {
            return _records.TryGetValue(
                    Key(domain, occurrenceId),
                    out var record)
                ? record
                : null;
        }
    }

    private static ContinuityOwnershipRecord Snapshot(
        ContinuityOwnershipRequest request,
        ContinuityOwnershipDisposition disposition,
        bool terminal,
        bool consumed,
        ContinuityRetirementReason? reason,
        DateTimeOffset now)
        => new(
            request,
            disposition,
            terminal,
            consumed,
            reason,
            now);

    private static string Key(
        ContinuityOwnershipDomain domain,
        string occurrenceId)
        => ((int)domain).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":"
            + occurrenceId;

    private void RemoveExpiredTerminalRecords(DateTimeOffset now)
    {
        var expired = _records
            .Where(
                pair => pair.Value.IsTerminal
                    && now >= pair.Value.Request.ExpiresAt)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired)
            _records.Remove(key);
    }
}
