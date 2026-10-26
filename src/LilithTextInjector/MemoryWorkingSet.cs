using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum MemoryConversationWriteDisposition
{
    Stored = 0,
    Duplicate = 1,
    Rejected = 2
}

internal sealed class MemoryConversationWriteTerminal
{
    internal MemoryConversationWriteDisposition Disposition { get; }
    internal Guid OccurrenceId { get; }
    internal Guid? CanonicalEventId { get; }
    internal MemoryWriteRejectionReason RejectionReason { get; }
    internal bool Accepted =>
        Disposition is MemoryConversationWriteDisposition.Stored
            or MemoryConversationWriteDisposition.Duplicate;

    internal MemoryConversationWriteTerminal(
        MemoryConversationWriteDisposition disposition,
        Guid occurrenceId,
        Guid? canonicalEventId,
        MemoryWriteRejectionReason rejectionReason)
    {
        if (!Enum.IsDefined(
                typeof(MemoryConversationWriteDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (occurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Memory occurrence identity cannot be empty.",
                nameof(occurrenceId));
        if (disposition
                is MemoryConversationWriteDisposition.Stored
                    or MemoryConversationWriteDisposition.Duplicate
            && !canonicalEventId.HasValue)
        {
            throw new ArgumentException(
                "Accepted Memory writes require a canonical event identity.",
                nameof(canonicalEventId));
        }
        Disposition = disposition;
        OccurrenceId = occurrenceId;
        CanonicalEventId = canonicalEventId;
        RejectionReason = rejectionReason;
    }
}

/// <summary>
/// Exact process-epoch receipt for one conversation occurrence admitted to
/// the Memory writer queue. Queue admission alone is never treated as a
/// successful Memory write.
/// </summary>
internal sealed class MemoryConversationWriteReceipt
{
    private readonly TaskCompletionSource<MemoryConversationWriteTerminal>
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal string ProcessEpoch { get; }
    internal Guid OccurrenceId { get; }
    internal Task<MemoryConversationWriteTerminal> Completion =>
        _completion.Task;

    internal MemoryConversationWriteReceipt(
        string processEpoch,
        Guid occurrenceId)
    {
        ProcessEpoch = MemoryWorkingSetGuard.Identifier(
            processEpoch,
            nameof(processEpoch));
        if (occurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Memory occurrence identity cannot be empty.",
                nameof(occurrenceId));
        OccurrenceId = occurrenceId;
    }

    internal bool TryComplete(MemoryConversationWriteTerminal terminal)
    {
        if (terminal == null)
            throw new ArgumentNullException(nameof(terminal));
        if (terminal.OccurrenceId != OccurrenceId)
            throw new ArgumentException(
                "Memory write terminal does not match its receipt.",
                nameof(terminal));
        return _completion.TrySetResult(terminal);
    }

    internal async ValueTask<MemoryConversationWriteTerminal> WaitAsync(
        CancellationToken cancellationToken)
        => await _completion.Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
}

internal enum MemoryWorkingRole
{
    User = 0,
    Assistant = 1
}

internal sealed class MemoryWorkingOccurrence
{
    internal Guid OccurrenceId { get; }
    internal Guid CanonicalEventId { get; }
    internal long Sequence { get; }
    internal long Segment { get; }
    internal string OwnerId { get; }
    internal string SessionId { get; }
    internal MemoryWorkingRole Role { get; }
    internal string ContentHash { get; }
    internal string Text { get; }
    internal bool IsBoundaryControl { get; }

    internal MemoryWorkingOccurrence(
        Guid occurrenceId,
        Guid canonicalEventId,
        long sequence,
        long segment,
        string ownerId,
        string sessionId,
        MemoryWorkingRole role,
        string contentHash,
        string text,
        bool isBoundaryControl)
    {
        if (occurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Working occurrence identity cannot be empty.",
                nameof(occurrenceId));
        if (canonicalEventId == Guid.Empty)
            throw new ArgumentException(
                "Working canonical event identity cannot be empty.",
                nameof(canonicalEventId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (segment <= 0)
            throw new ArgumentOutOfRangeException(nameof(segment));
        if (!Enum.IsDefined(typeof(MemoryWorkingRole), role))
            throw new ArgumentOutOfRangeException(nameof(role));
        OccurrenceId = occurrenceId;
        CanonicalEventId = canonicalEventId;
        Sequence = sequence;
        Segment = segment;
        OwnerId = MemoryWorkingSetGuard.Identifier(ownerId, nameof(ownerId));
        SessionId = MemoryWorkingSetGuard.Identifier(
            sessionId,
            nameof(sessionId));
        Role = role;
        ContentHash = MemoryWorkingSetGuard.Sha256(
            contentHash,
            nameof(contentHash));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        IsBoundaryControl = isBoundaryControl;
    }

    internal LivingPromptTurn ToPromptTurn()
        => new(
            OccurrenceId.ToString("N"),
            Role == MemoryWorkingRole.Assistant
                ? LivingPromptTurnRole.Assistant
                : LivingPromptTurnRole.User,
            ContentHash,
            Text,
            LivingPromptTurnOrigin.MemoryWorkingSet);
}

internal enum MemoryBoundaryScope
{
    Topic = 0,
    CurrentSegment = 1,
    AllHistorical = 2
}

internal sealed class MemoryDoNotDiscussBoundary
{
    internal Guid BoundaryId { get; }
    internal Guid SourceOccurrenceId { get; }
    internal long Revision { get; }
    internal MemoryBoundaryScope Scope { get; }
    internal IReadOnlySet<string> TopicTerms { get; }
    internal IReadOnlySet<Guid> BlockedOccurrenceIds { get; }
    internal IReadOnlySet<Guid> BlockedCanonicalEventIds { get; }
    internal IReadOnlySet<string> BlockedContentHashes { get; }

    internal MemoryDoNotDiscussBoundary(
        Guid boundaryId,
        Guid sourceOccurrenceId,
        long revision,
        MemoryBoundaryScope scope,
        IEnumerable<string>? topicTerms,
        IEnumerable<Guid>? blockedOccurrenceIds,
        IEnumerable<Guid>? blockedCanonicalEventIds,
        IEnumerable<string>? blockedContentHashes)
    {
        if (boundaryId == Guid.Empty)
            throw new ArgumentException(
                "Boundary identity cannot be empty.",
                nameof(boundaryId));
        if (sourceOccurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Boundary source identity cannot be empty.",
                nameof(sourceOccurrenceId));
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(typeof(MemoryBoundaryScope), scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        BoundaryId = boundaryId;
        SourceOccurrenceId = sourceOccurrenceId;
        Revision = revision;
        Scope = scope;
        TopicTerms = new HashSet<string>(
            topicTerms ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        BlockedOccurrenceIds = new HashSet<Guid>(
            blockedOccurrenceIds ?? Array.Empty<Guid>());
        BlockedCanonicalEventIds = new HashSet<Guid>(
            blockedCanonicalEventIds ?? Array.Empty<Guid>());
        BlockedContentHashes = new HashSet<string>(
            blockedContentHashes ?? Array.Empty<string>(),
            StringComparer.Ordinal);
    }

    internal bool Matches(MemoryWorkingOccurrence occurrence)
    {
        if (occurrence == null)
            throw new ArgumentNullException(nameof(occurrence));
        if (occurrence.OccurrenceId == SourceOccurrenceId
            || BlockedOccurrenceIds.Contains(occurrence.OccurrenceId)
            || BlockedCanonicalEventIds.Contains(
                occurrence.CanonicalEventId)
            || BlockedContentHashes.Contains(occurrence.ContentHash))
        {
            return true;
        }
        return Scope == MemoryBoundaryScope.AllHistorical
            || Scope == MemoryBoundaryScope.Topic
                && MemoryBoundaryMatcher.MatchesTopic(
                    TopicTerms,
                    occurrence.Text);
    }

    internal bool Matches(RetrievedMemory memory)
    {
        if (memory == null)
            throw new ArgumentNullException(nameof(memory));
        var contentHash = LongTermMemoryPacketAssembler.ContentHash(
            memory.ContextText);
        if (BlockedCanonicalEventIds.Contains(memory.Event.Id)
            || BlockedContentHashes.Contains(contentHash))
        {
            return true;
        }
        return Scope == MemoryBoundaryScope.AllHistorical
            || Scope == MemoryBoundaryScope.Topic
                && MemoryBoundaryMatcher.MatchesTopic(
                    TopicTerms,
                    memory.ContextText);
    }
}

internal sealed class MemoryOpenLoop
{
    internal const int MaximumNudges = 0;

    internal Guid LoopId { get; }
    internal Guid SourceOccurrenceId { get; }
    internal long Revision { get; }
    internal string QuestionHash { get; }
    internal bool CanNudge => false;
    internal int NudgeCount => 0;

    internal MemoryOpenLoop(
        Guid loopId,
        Guid sourceOccurrenceId,
        long revision,
        string questionHash)
    {
        if (loopId == Guid.Empty)
            throw new ArgumentException(
                "Open-loop identity cannot be empty.",
                nameof(loopId));
        if (sourceOccurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Open-loop source identity cannot be empty.",
                nameof(sourceOccurrenceId));
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        LoopId = loopId;
        SourceOccurrenceId = sourceOccurrenceId;
        Revision = revision;
        QuestionHash = MemoryWorkingSetGuard.Sha256(
            questionHash,
            nameof(questionHash));
    }
}

internal sealed class MemoryWorkingSetSnapshot
{
    internal string ProcessEpoch { get; }
    internal long Revision { get; }
    internal IReadOnlyList<MemoryWorkingOccurrence> Occurrences { get; }
    internal IReadOnlyList<MemoryDoNotDiscussBoundary> Boundaries { get; }
    internal MemoryOpenLoop? OpenLoop { get; }
    internal bool FailSafeHistoricalSuppression { get; }
    internal string Fingerprint { get; }

    internal MemoryWorkingSetSnapshot(
        string processEpoch,
        long revision,
        IEnumerable<MemoryWorkingOccurrence> occurrences,
        IEnumerable<MemoryDoNotDiscussBoundary> boundaries,
        MemoryOpenLoop? openLoop,
        bool failSafeHistoricalSuppression)
    {
        ProcessEpoch = MemoryWorkingSetGuard.Identifier(
            processEpoch,
            nameof(processEpoch));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        Revision = revision;
        Occurrences = new ReadOnlyCollection<MemoryWorkingOccurrence>(
            (occurrences ?? throw new ArgumentNullException(
                nameof(occurrences))).ToArray());
        Boundaries = new ReadOnlyCollection<MemoryDoNotDiscussBoundary>(
            (boundaries ?? throw new ArgumentNullException(
                nameof(boundaries))).ToArray());
        OpenLoop = openLoop;
        FailSafeHistoricalSuppression = failSafeHistoricalSuppression;
        Fingerprint = LongTermMemoryPacketAssembler.Hash(
            string.Join(
                "|",
                ProcessEpoch,
                Revision,
                string.Join(
                    ",",
                    Occurrences.Select(
                        item => item.OccurrenceId.ToString("N")
                            + ":"
                            + item.Sequence
                            + ":"
                            + item.ContentHash)),
                string.Join(
                    ",",
                    Boundaries.Select(
                        item => item.BoundaryId.ToString("N")
                            + ":"
                            + (int)item.Scope)),
                OpenLoop?.LoopId.ToString("N") ?? "-",
                FailSafeHistoricalSuppression ? "1" : "0"));
    }
}

internal enum MemoryConversationContextPurpose
{
    OrdinaryPlayerReply = 0,
    ContextualProactive = 1
}

internal enum MemoryConversationContextAdmission
{
    Ready = 0,
    CurrentWriteUnavailable = 1,
    ProactiveSuppressedByBoundary = 2,
    ProactiveSuppressedByOpenLoop = 3,
    ConversationWriteUnhealthy = 4
}

internal enum MemoryProactiveGuardReason
{
    None = 0,
    DoNotDiscussBoundary = 1,
    OpenLoopWaitingForPlayer = 2
}

internal sealed class MemoryProactiveGuardSnapshot
{
    internal string ProcessEpoch { get; }
    internal long Revision { get; }
    internal MemoryProactiveGuardReason Reason { get; }
    internal bool AllowsProactive =>
        Reason == MemoryProactiveGuardReason.None;

    internal MemoryProactiveGuardSnapshot(
        string processEpoch,
        long revision,
        MemoryProactiveGuardReason reason)
    {
        ProcessEpoch = MemoryWorkingSetGuard.Identifier(
            processEpoch,
            nameof(processEpoch));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(typeof(MemoryProactiveGuardReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Revision = revision;
        Reason = reason;
    }
}

internal sealed class MemoryConversationContextRead
{
    internal MemoryConversationContextAdmission Admission { get; }
    internal MemoryWorkingSetSnapshot Snapshot { get; }
    internal MemoryRetrievalResult Retrieval { get; }
    internal IReadOnlyList<MemoryWorkingOccurrence> WorkingOccurrences {
        get;
    }
    internal Guid? CurrentCanonicalEventId { get; }
    internal int WorkingItemsExcludedByBoundary { get; }
    internal int RetrievalItemsExcludedByBoundary { get; }
    internal int ProviderCallDelta => 0;
    internal bool Ready =>
        Admission == MemoryConversationContextAdmission.Ready;

    internal MemoryConversationContextRead(
        MemoryConversationContextAdmission admission,
        MemoryWorkingSetSnapshot snapshot,
        MemoryRetrievalResult retrieval,
        IEnumerable<MemoryWorkingOccurrence> workingOccurrences,
        Guid? currentCanonicalEventId,
        int workingItemsExcludedByBoundary,
        int retrievalItemsExcludedByBoundary)
    {
        if (!Enum.IsDefined(
                typeof(MemoryConversationContextAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        Admission = admission;
        Snapshot = snapshot
            ?? throw new ArgumentNullException(nameof(snapshot));
        Retrieval = retrieval
            ?? throw new ArgumentNullException(nameof(retrieval));
        WorkingOccurrences =
            new ReadOnlyCollection<MemoryWorkingOccurrence>(
                (workingOccurrences
                    ?? throw new ArgumentNullException(
                        nameof(workingOccurrences))).ToArray());
        CurrentCanonicalEventId = currentCanonicalEventId;
        WorkingItemsExcludedByBoundary = Math.Max(
            0,
            workingItemsExcludedByBoundary);
        RetrievalItemsExcludedByBoundary = Math.Max(
            0,
            retrievalItemsExcludedByBoundary);
    }
}

/// <summary>
/// Memory-owned, process-local continuity reducer. It never reads or writes a
/// file and intentionally starts empty after every process restart. Ordering
/// is the single Memory writer's monotonic sequence, never mutable wall time.
/// </summary>
internal sealed class MemoryWorkingSetReducer
{
    internal const int MaximumTurns = 12;
    internal const int MaximumTotalScalars = 2_400;
    internal const int MaximumTurnScalars = 800;
    internal const int MaximumBoundaries = 8;
    internal const int MaximumControlIdentities = 32;

    private readonly object _sync = new();
    private readonly string _ownerId;
    private readonly string _sessionId;
    private readonly string _processEpoch;
    private readonly List<MemoryWorkingOccurrence> _occurrences = new();
    private readonly List<MemoryDoNotDiscussBoundary> _boundaries = new();
    private readonly HashSet<Guid> _controlCanonicalEventIds = new();
    private readonly HashSet<string> _controlContentHashes = new(
        StringComparer.Ordinal);
    private long _sequence;
    private long _revision;
    private long _segment = 1;
    private MemoryOpenLoop? _openLoop;
    private bool _failSafeHistoricalSuppression;

    internal MemoryWorkingSetReducer(
        string ownerId,
        string sessionId,
        string? processEpoch = null)
    {
        _ownerId = MemoryWorkingSetGuard.Identifier(
            ownerId,
            nameof(ownerId));
        _sessionId = MemoryWorkingSetGuard.Identifier(
            sessionId,
            nameof(sessionId));
        _processEpoch = MemoryWorkingSetGuard.Identifier(
            processEpoch ?? "memory-epoch-" + Guid.NewGuid().ToString("N"),
            nameof(processEpoch));
    }

    internal string ProcessEpoch => _processEpoch;

    internal bool Apply(
        MemoryWriteProposal proposal,
        MemoryWriteOutcome outcome)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (outcome == null)
            throw new ArgumentNullException(nameof(outcome));
        if (proposal.Id != outcome.ProposalId
            || !outcome.EventId.HasValue
            || (!outcome.Stored
                && outcome.RejectionReason
                    != MemoryWriteRejectionReason.Duplicate)
            || proposal.Source != MemoryProposalSource.LocalRuntime
            || proposal.Kind != MemoryEventKind.ConversationTurn
            || !string.Equals(
                proposal.OwnerId,
                _ownerId,
                StringComparison.Ordinal)
            || !string.Equals(
                proposal.SessionId,
                _sessionId,
                StringComparison.Ordinal)
            || proposal.Sensitivity
                is not MemorySensitivity.Normal
                    and not MemorySensitivity.Personal)
        {
            return false;
        }
        var role = string.Equals(
                proposal.Role,
                "user",
                StringComparison.Ordinal)
            ? MemoryWorkingRole.User
            : string.Equals(
                proposal.Role,
                "model",
                StringComparison.Ordinal)
                ? MemoryWorkingRole.Assistant
                : (MemoryWorkingRole?)null;
        if (!role.HasValue)
            return false;

        lock (_sync)
        {
            if (_occurrences.Any(
                    item => item.OccurrenceId == proposal.Id))
            {
                return true;
            }
            var sequence = checked(++_sequence);
            var normalized = MemoryWorkingSetGuard.NormalizeText(
                proposal.Content);
            var bounded = MemoryWorkingSetGuard.TakeScalars(
                normalized,
                MaximumTurnScalars);
            var isBoundaryControl = false;

            if (role == MemoryWorkingRole.User)
            {
                _openLoop = null;
                if (MemoryBoundaryMatcher.TryParseRelease(
                        bounded,
                        out var releasedTopic,
                        out var releaseCurrentSegment))
                {
                    isBoundaryControl = true;
                    ReleaseBoundaries(
                        releasedTopic,
                        releaseCurrentSegment);
                }
                else if (MemoryBoundaryMatcher.TryParseBoundary(
                        bounded,
                        out var topic,
                        out var currentSegment))
                {
                    var segmentOccurrences = _occurrences
                        .Where(item => item.Segment == _segment)
                        .ToArray();
                    if (!currentSegment
                        || segmentOccurrences.Length > 0)
                    {
                        isBoundaryControl = true;
                        AddBoundary(
                            proposal.Id,
                            outcome.EventId.Value,
                            sequence,
                            topic,
                            currentSegment,
                            segmentOccurrences);
                        _segment = checked(_segment + 1);
                    }
                }
            }

            var occurrence = new MemoryWorkingOccurrence(
                proposal.Id,
                outcome.EventId.Value,
                sequence,
                _segment,
                _ownerId,
                _sessionId,
                role.Value,
                LongTermMemoryPacketAssembler.ContentHash(bounded),
                bounded,
                isBoundaryControl);
            _occurrences.Add(occurrence);
            if (isBoundaryControl)
            {
                TrackControlIdentity(
                    occurrence.CanonicalEventId,
                    occurrence.ContentHash);
            }

            if (role == MemoryWorkingRole.Assistant
                && MemoryBoundaryMatcher.IsAssistantQuestion(bounded))
            {
                _openLoop = new MemoryOpenLoop(
                    Guid.NewGuid(),
                    proposal.Id,
                    checked(_revision + 1),
                    occurrence.ContentHash);
            }

            TrimOccurrences();
            _revision = checked(_revision + 1);
            return true;
        }
    }

    internal MemoryWorkingSetSnapshot GetSnapshot()
    {
        lock (_sync)
            return SnapshotUnsafe();
    }

    internal MemoryProactiveGuardSnapshot GetProactiveGuard()
    {
        lock (_sync)
        {
            var reason = _boundaries.Count > 0
                    || _failSafeHistoricalSuppression
                ? MemoryProactiveGuardReason.DoNotDiscussBoundary
                : _openLoop != null
                    ? MemoryProactiveGuardReason.OpenLoopWaitingForPlayer
                    : MemoryProactiveGuardReason.None;
            return new MemoryProactiveGuardSnapshot(
                _processEpoch,
                _revision,
                reason);
        }
    }

    internal MemoryConversationContextRead Project(
        MemoryRetrievalResult retrieval,
        MemoryConversationContextPurpose purpose,
        MemoryConversationWriteTerminal? currentWrite,
        bool conversationWritesHealthy = true)
    {
        if (retrieval == null)
            throw new ArgumentNullException(nameof(retrieval));
        if (!Enum.IsDefined(
                typeof(MemoryConversationContextPurpose),
                purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        lock (_sync)
        {
            var snapshot = SnapshotUnsafe();
            if (!conversationWritesHealthy)
            {
                return Empty(
                    MemoryConversationContextAdmission
                        .ConversationWriteUnhealthy,
                    snapshot,
                    retrieval,
                    currentWrite?.CanonicalEventId);
            }
            if (purpose
                    == MemoryConversationContextPurpose.OrdinaryPlayerReply
                && (currentWrite == null
                    || !currentWrite.Accepted
                    || !_occurrences.Any(
                        item => item.OccurrenceId
                                == currentWrite.OccurrenceId
                            && item.CanonicalEventId
                                == currentWrite.CanonicalEventId)))
            {
                return Empty(
                    MemoryConversationContextAdmission
                        .CurrentWriteUnavailable,
                    snapshot,
                    retrieval,
                    currentWrite?.CanonicalEventId);
            }
            if (purpose
                    == MemoryConversationContextPurpose.ContextualProactive
                && (_boundaries.Count > 0
                    || _failSafeHistoricalSuppression))
            {
                return Empty(
                    MemoryConversationContextAdmission
                        .ProactiveSuppressedByBoundary,
                    snapshot,
                    retrieval,
                    null);
            }
            if (purpose
                    == MemoryConversationContextPurpose.ContextualProactive
                && _openLoop != null)
            {
                return Empty(
                    MemoryConversationContextAdmission
                        .ProactiveSuppressedByOpenLoop,
                    snapshot,
                    retrieval,
                    null);
            }

            var currentOccurrence = currentWrite == null
                ? null
                : _occurrences.FirstOrDefault(
                    item => item.OccurrenceId
                            == currentWrite.OccurrenceId
                        && item.CanonicalEventId
                            == currentWrite.CanonicalEventId);
            var workingNewestFirst =
                new List<MemoryWorkingOccurrence>();
            var seenCanonicalEventIds = new HashSet<Guid>();
            var seenContentHashes = new HashSet<string>(
                StringComparer.Ordinal);
            var workingExcludedByBoundary = 0;
            for (var index = _occurrences.Count - 1;
                 index >= 0;
                 index--)
            {
                var occurrence = _occurrences[index];
                if (occurrence.OccurrenceId
                    == currentOccurrence?.OccurrenceId)
                    continue;
                if (occurrence.IsBoundaryControl
                    || IsBlocked(occurrence))
                {
                    workingExcludedByBoundary++;
                    continue;
                }
                if (currentOccurrence != null
                    && (occurrence.CanonicalEventId
                            == currentOccurrence.CanonicalEventId
                        || string.Equals(
                            occurrence.ContentHash,
                            currentOccurrence.ContentHash,
                            StringComparison.Ordinal)))
                {
                    continue;
                }
                if (seenCanonicalEventIds.Contains(
                        occurrence.CanonicalEventId)
                    || seenContentHashes.Contains(
                        occurrence.ContentHash))
                {
                    continue;
                }
                seenCanonicalEventIds.Add(occurrence.CanonicalEventId);
                seenContentHashes.Add(occurrence.ContentHash);
                workingNewestFirst.Add(occurrence);
            }
            workingNewestFirst.Reverse();

            var filteredItems = new List<RetrievedMemory>();
            var retrievalExcluded = 0;
            foreach (var item in retrieval.Items)
            {
                if (IsBlocked(item))
                {
                    retrievalExcluded++;
                    continue;
                }
                filteredItems.Add(item);
            }
            var filteredRetrieval = new MemoryRetrievalResult(
                filteredItems,
                filteredItems.Sum(item => item.ContextText.Length),
                retrieval.SensitiveItemsExcluded,
                retrieval.BudgetItemsExcluded);
            return new MemoryConversationContextRead(
                MemoryConversationContextAdmission.Ready,
                snapshot,
                filteredRetrieval,
                workingNewestFirst,
                currentWrite?.CanonicalEventId,
                workingExcludedByBoundary,
                retrievalExcluded);
        }
    }

    private void AddBoundary(
        Guid sourceOccurrenceId,
        Guid sourceCanonicalEventId,
        long revision,
        string topic,
        bool currentSegment,
        IReadOnlyList<MemoryWorkingOccurrence> segmentOccurrences)
    {
        if (_boundaries.Count >= MaximumBoundaries)
        {
            _failSafeHistoricalSuppression = true;
            return;
        }
        var terms = currentSegment
            ? Array.Empty<string>()
            : MemoryTextTerms.Extract(topic).ToArray();
        if (!currentSegment && terms.Length == 0)
            return;
        _boundaries.Add(
            new MemoryDoNotDiscussBoundary(
                Guid.NewGuid(),
                sourceOccurrenceId,
                revision,
                currentSegment
                    ? MemoryBoundaryScope.CurrentSegment
                    : MemoryBoundaryScope.Topic,
                terms,
                currentSegment
                    ? segmentOccurrences.Select(item => item.OccurrenceId)
                    : Array.Empty<Guid>(),
                currentSegment
                    ? segmentOccurrences.Select(
                        item => item.CanonicalEventId)
                        .Append(sourceCanonicalEventId)
                    : new[] { sourceCanonicalEventId },
                currentSegment
                    ? segmentOccurrences.Select(item => item.ContentHash)
                    : Array.Empty<string>()));
    }

    private void ReleaseBoundaries(
        string topic,
        bool currentSegment)
    {
        if (currentSegment)
        {
            for (var index = _boundaries.Count - 1;
                 index >= 0;
                 index--)
            {
                if (_boundaries[index].Scope
                    == MemoryBoundaryScope.CurrentSegment)
                {
                    _boundaries.RemoveAt(index);
                    break;
                }
            }
            return;
        }
        var releaseTerms = MemoryTextTerms.Extract(topic);
        _boundaries.RemoveAll(
            boundary => boundary.Scope == MemoryBoundaryScope.Topic
                && boundary.TopicTerms.SetEquals(releaseTerms));
    }

    private bool IsBlocked(MemoryWorkingOccurrence occurrence)
        => _failSafeHistoricalSuppression
            || _boundaries.Any(boundary => boundary.Matches(occurrence));

    private bool IsBlocked(RetrievedMemory memory)
    {
        if (_failSafeHistoricalSuppression
            || _controlCanonicalEventIds.Contains(memory.Event.Id))
        {
            return true;
        }
        var contentHash = LongTermMemoryPacketAssembler.ContentHash(
            memory.ContextText);
        return _controlContentHashes.Contains(contentHash)
            || _boundaries.Any(boundary => boundary.Matches(memory));
    }

    private void TrackControlIdentity(
        Guid canonicalEventId,
        string contentHash)
    {
        if (_controlCanonicalEventIds.Contains(canonicalEventId)
            && _controlContentHashes.Contains(contentHash))
        {
            return;
        }
        if (_controlCanonicalEventIds.Count >= MaximumControlIdentities
            || _controlContentHashes.Count >= MaximumControlIdentities)
        {
            _failSafeHistoricalSuppression = true;
            return;
        }
        _controlCanonicalEventIds.Add(canonicalEventId);
        _controlContentHashes.Add(contentHash);
    }

    private void TrimOccurrences()
    {
        while (_occurrences.Count > MaximumTurns)
            _occurrences.RemoveAt(0);
        var scalars = _occurrences.Sum(
            item => LongTermMemoryPacketAssembler.UnicodeScalarCount(
                item.Text));
        while (_occurrences.Count > 1
            && scalars > MaximumTotalScalars)
        {
            scalars -= LongTermMemoryPacketAssembler.UnicodeScalarCount(
                _occurrences[0].Text);
            _occurrences.RemoveAt(0);
        }
    }

    private MemoryWorkingSetSnapshot SnapshotUnsafe()
        => new(
            _processEpoch,
            _revision,
            _occurrences,
            _boundaries,
            _openLoop,
            _failSafeHistoricalSuppression);

    private static MemoryConversationContextRead Empty(
        MemoryConversationContextAdmission admission,
        MemoryWorkingSetSnapshot snapshot,
        MemoryRetrievalResult retrieval,
        Guid? currentCanonicalEventId)
        => new(
            admission,
            snapshot,
            new MemoryRetrievalResult(
                Array.Empty<RetrievedMemory>(),
                0,
                retrieval.SensitiveItemsExcluded,
                retrieval.BudgetItemsExcluded),
            Array.Empty<MemoryWorkingOccurrence>(),
            currentCanonicalEventId,
            0,
            retrieval.Items.Count);
}

internal static class MemoryBoundaryMatcher
{
    private static readonly Regex ChineseBoundary = new(
        @"^(?:莉莉絲|莉莉丝)?\s*[,，：:]?\s*(?:我\s*)?(?:不想|不要|別|别)\s*(?:再\s*)?(?:談|谈|聊|提)(?:論|论|起)?\s*(?<topic>.+?)\s*[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishBoundary = new(
        @"^(?:lilith\s*[,,:]?\s*)?(?:please\s+)?(?:do\s+not|don't|dont|stop)\s+(?:talk(?:ing)?|discuss(?:ing)?|mention(?:ing)?)\s+(?:about\s+)?(?<topic>.+?)\s*[.!]?$",
        RegexOptions.Compiled
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase);
    private static readonly Regex ChineseRelease = new(
        @"^(?:莉莉絲|莉莉丝)?\s*[,，：:]?\s*(?:我們|我们)?\s*(?:可以|能夠|能够)\s*(?:再\s*)?(?:談|谈|聊|提)(?:論|论|起)?\s*(?<topic>.+?)\s*[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishRelease = new(
        @"^(?:lilith\s*[,,:]?\s*)?(?:we\s+)?(?:can|may)\s+(?:talk|discuss|mention)\s+(?:about\s+)?(?<topic>.+?)\s*[.!]?$",
        RegexOptions.Compiled
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> DeicticTopics = new(
        new[]
        {
            "這件事",
            "这件事",
            "這個話題",
            "这个话题",
            "這件事情",
            "这件事情",
            "this",
            "that",
            "it",
            "this topic",
            "that topic"
        },
        StringComparer.OrdinalIgnoreCase);

    internal static bool TryParseBoundary(
        string text,
        out string topic,
        out bool currentSegment)
        => TryParse(
            text,
            ChineseBoundary,
            EnglishBoundary,
            out topic,
            out currentSegment);

    internal static bool TryParseRelease(
        string text,
        out string topic,
        out bool currentSegment)
        => TryParse(
            text,
            ChineseRelease,
            EnglishRelease,
            out topic,
            out currentSegment);

    internal static bool IsAssistantQuestion(string text)
    {
        var normalized = MemoryWorkingSetGuard.NormalizeText(text);
        return normalized.Length >= 2
            && (normalized.EndsWith("?", StringComparison.Ordinal)
                || normalized.EndsWith("？", StringComparison.Ordinal));
    }

    internal static bool MatchesTopic(
        IReadOnlySet<string> topicTerms,
        string candidate)
    {
        if (topicTerms == null)
            throw new ArgumentNullException(nameof(topicTerms));
        if (topicTerms.Count == 0)
            return false;
        return topicTerms.Overlaps(MemoryTextTerms.Extract(candidate));
    }

    private static bool TryParse(
        string text,
        Regex chinese,
        Regex english,
        out string topic,
        out bool currentSegment)
    {
        topic = string.Empty;
        currentSegment = false;
        var normalized = MemoryWorkingSetGuard.NormalizeText(text);
        if (normalized.Length == 0)
            return false;
        var match = chinese.Match(normalized);
        if (!match.Success)
            match = english.Match(normalized);
        if (!match.Success)
            return false;
        topic = match.Groups["topic"].Value
            .Trim()
            .TrimEnd('了', '吧', '.', '!', '。', '！');
        if (topic.Length == 0)
            return false;
        currentSegment = DeicticTopics.Contains(topic);
        return currentSegment
            || MemoryTextTerms.Extract(topic).Count > 0;
    }
}

internal static class MemoryWorkingSetGuard
{
    internal static string Identifier(string? value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0
            || normalized.Length > 160
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Memory working-set identifier is invalid.",
                parameterName);
        }
        return normalized;
    }

    internal static string Sha256(string value, string parameterName)
    {
        if (value == null
            || value.Length != 64
            || value.Any(
                character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Memory working-set hash is invalid.",
                parameterName);
        }
        return value.ToUpperInvariant();
    }

    internal static string NormalizeText(string? value)
        => (value ?? string.Empty)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormKC)
            .Trim();

    internal static string TakeScalars(string value, int maximumScalars)
    {
        if (maximumScalars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumScalars));
        if (LongTermMemoryPacketAssembler.UnicodeScalarCount(value)
            <= maximumScalars)
        {
            return value;
        }
        var builder = new StringBuilder();
        var scalars = 0;
        for (var index = 0;
             index < value.Length && scalars < maximumScalars;
             index++)
        {
            builder.Append(value[index]);
            if (char.IsHighSurrogate(value[index])
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                builder.Append(value[++index]);
            }
            scalars++;
        }
        return builder.ToString();
    }
}
