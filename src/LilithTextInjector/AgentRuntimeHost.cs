using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace LilithTextInjector;

internal enum AgentLanguageSourceKind
{
    OrdinaryPlayerTurn = 1,
    LocalTimer = 3
}

internal enum AgentLanguageArbitrationDisposition
{
    Selected = 1,
    Deferred = 2,
    Rejected = 3
}

internal enum AgentLanguageArbitrationReason
{
    None = 0,
    HigherPriorityProposal = 1,
    ActivePlayerTurn = 2,
    PlayerInputPending = 3,
    DialogueBusy = 4,
    EarlierOutputSelected = 5,
    RuntimeGenerationChanged = 6,
    RuntimeEpochChanged = 7,
    NotReady = 8,
    Expired = 9,
    DuplicateProposal = 10,
    MonotonicClockRejected = 11,
    ForeignProposal = 12,
    EarlierProposal = 13
}

internal sealed class AgentRuntimeDeadline
{
    private readonly object _issuerToken;

    internal string DeadlineId { get; }
    internal string ProcessEpochId { get; }
    internal long Sequence { get; }
    internal long DueTimestamp { get; }

    internal AgentRuntimeDeadline(
        string deadlineId,
        string processEpochId,
        long sequence,
        long dueTimestamp,
        object issuerToken)
    {
        DeadlineId = AgentRuntimeGuard.Metadata(
            deadlineId,
            nameof(deadlineId));
        ProcessEpochId = AgentRuntimeGuard.Metadata(
            processEpochId,
            nameof(processEpochId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (dueTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(dueTimestamp));
        Sequence = sequence;
        DueTimestamp = dueTimestamp;
        _issuerToken = issuerToken
            ?? throw new ArgumentNullException(nameof(issuerToken));
    }

    internal bool IsIssuedBy(object issuerToken)
        => ReferenceEquals(_issuerToken, issuerToken);
}

internal sealed class AgentLanguageProposal
{
    private readonly object _issuerToken;
    private bool _claimed;

    internal string ProposalId { get; }
    internal string ProcessEpochId { get; }
    internal long Sequence { get; }
    internal long RuntimeGeneration { get; }
    internal AgentLanguageSourceKind SourceKind { get; }
    internal string SourceCorrelationId { get; }
    internal long CreatedTimestamp { get; }
    internal long NotBeforeTimestamp { get; }
    internal long? ExpiresTimestamp { get; }

    internal AgentLanguageProposal(
        string proposalId,
        string processEpochId,
        long sequence,
        long runtimeGeneration,
        AgentLanguageSourceKind sourceKind,
        string sourceCorrelationId,
        long createdTimestamp,
        long notBeforeTimestamp,
        long? expiresTimestamp,
        object issuerToken)
    {
        ProposalId = AgentRuntimeGuard.Metadata(
            proposalId,
            nameof(proposalId));
        ProcessEpochId = AgentRuntimeGuard.Metadata(
            processEpochId,
            nameof(processEpochId));
        SourceCorrelationId = AgentRuntimeGuard.Metadata(
            sourceCorrelationId,
            nameof(sourceCorrelationId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        if (!Enum.IsDefined(typeof(AgentLanguageSourceKind), sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (createdTimestamp < 0
            || notBeforeTimestamp < createdTimestamp
            || (expiresTimestamp.HasValue
                && expiresTimestamp.Value <= notBeforeTimestamp))
        {
            throw new ArgumentOutOfRangeException(nameof(createdTimestamp));
        }
        Sequence = sequence;
        RuntimeGeneration = runtimeGeneration;
        SourceKind = sourceKind;
        CreatedTimestamp = createdTimestamp;
        NotBeforeTimestamp = notBeforeTimestamp;
        ExpiresTimestamp = expiresTimestamp;
        _issuerToken = issuerToken
            ?? throw new ArgumentNullException(nameof(issuerToken));
    }

    internal bool Matches(AgentLanguageProposal? other)
        => other != null
            && Sequence == other.Sequence
            && RuntimeGeneration == other.RuntimeGeneration
            && SourceKind == other.SourceKind
            && string.Equals(
                ProposalId,
                other.ProposalId,
                StringComparison.Ordinal)
            && string.Equals(
                ProcessEpochId,
                other.ProcessEpochId,
                StringComparison.Ordinal)
            && string.Equals(
                SourceCorrelationId,
                other.SourceCorrelationId,
                StringComparison.Ordinal)
            && CreatedTimestamp == other.CreatedTimestamp
            && NotBeforeTimestamp == other.NotBeforeTimestamp
            && ExpiresTimestamp == other.ExpiresTimestamp;

    internal bool IsIssuedBy(object issuerToken)
        => ReferenceEquals(_issuerToken, issuerToken);

    internal bool IsClaimed => _claimed;

    internal bool TryMarkClaimed(object issuerToken)
    {
        if (!IsIssuedBy(issuerToken) || _claimed)
            return false;
        _claimed = true;
        return true;
    }
}

internal sealed class AgentLanguageProposalOutcome
{
    internal AgentLanguageProposal Proposal { get; }
    internal AgentLanguageArbitrationDisposition Disposition { get; }
    internal AgentLanguageArbitrationReason Reason { get; }
    internal string WinningProposalId { get; }
    internal bool NoFallback => true;

    internal AgentLanguageProposalOutcome(
        AgentLanguageProposal proposal,
        AgentLanguageArbitrationDisposition disposition,
        AgentLanguageArbitrationReason reason,
        string winningProposalId)
    {
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
        if (!Enum.IsDefined(
                typeof(AgentLanguageArbitrationDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (!Enum.IsDefined(typeof(AgentLanguageArbitrationReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if ((disposition == AgentLanguageArbitrationDisposition.Selected)
            != (reason == AgentLanguageArbitrationReason.None))
        {
            throw new ArgumentException(
                "Selected language output requires the None reason.");
        }
        Disposition = disposition;
        Reason = reason;
        WinningProposalId = (winningProposalId ?? string.Empty).Length == 0
            ? string.Empty
            : AgentRuntimeGuard.Metadata(
                winningProposalId,
                nameof(winningProposalId));
        if (disposition == AgentLanguageArbitrationDisposition.Selected
            && !string.Equals(
                WinningProposalId,
                proposal.ProposalId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The selected proposal must identify itself as winner.",
                nameof(winningProposalId));
        }
    }
}

internal sealed class AgentLanguageArbitrationOutcome
{
    internal string OutcomeId { get; }
    internal string ProcessEpochId { get; }
    internal long CycleSequence { get; }
    internal long ObservedTimestamp { get; }
    internal IReadOnlyList<AgentLanguageProposalOutcome> Proposals { get; }
    internal AgentLanguageProposal? SelectedProposal { get; }
    internal bool NoFallback => true;

    internal AgentLanguageArbitrationOutcome(
        string outcomeId,
        string processEpochId,
        long cycleSequence,
        long observedTimestamp,
        IEnumerable<AgentLanguageProposalOutcome> proposals,
        AgentLanguageProposal? selectedProposal)
    {
        OutcomeId = AgentRuntimeGuard.Metadata(outcomeId, nameof(outcomeId));
        ProcessEpochId = AgentRuntimeGuard.Metadata(
            processEpochId,
            nameof(processEpochId));
        if (cycleSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(cycleSequence));
        if (observedTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(observedTimestamp));
        CycleSequence = cycleSequence;
        ObservedTimestamp = observedTimestamp;
        Proposals = new ReadOnlyCollection<AgentLanguageProposalOutcome>(
            (proposals ?? throw new ArgumentNullException(nameof(proposals)))
                .ToArray());
        SelectedProposal = selectedProposal;
        var selectedOutcomes = Proposals
            .Where(item => item.Disposition
                == AgentLanguageArbitrationDisposition.Selected)
            .ToArray();
        if ((selectedProposal == null) != (selectedOutcomes.Length == 0)
            || selectedOutcomes.Length > 1
            || (selectedProposal != null
                && !ReferenceEquals(
                    selectedOutcomes[0].Proposal,
                    selectedProposal))
            || (selectedProposal != null
                && !string.Equals(
                    selectedOutcomes[0].WinningProposalId,
                    selectedProposal.ProposalId,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The selected proposal must match the proposal outcomes.");
        }
    }

    internal AgentLanguageProposalOutcome? Find(
        AgentLanguageProposal proposal)
        => Proposals.FirstOrDefault(
            item => ReferenceEquals(item.Proposal, proposal));
}

internal sealed class AgentLanguagePublicationLease
{
    internal string LeaseId { get; }
    internal string ProcessEpochId { get; }
    internal long CycleSequence { get; }
    internal AgentLanguageProposal Proposal { get; }

    internal AgentLanguagePublicationLease(
        string leaseId,
        string processEpochId,
        long cycleSequence,
        AgentLanguageProposal proposal)
    {
        LeaseId = AgentRuntimeGuard.Metadata(leaseId, nameof(leaseId));
        ProcessEpochId = AgentRuntimeGuard.Metadata(
            processEpochId,
            nameof(processEpochId));
        if (cycleSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(cycleSequence));
        CycleSequence = cycleSequence;
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
    }

    internal bool Matches(AgentLanguagePublicationLease? other)
        => other != null
            && CycleSequence == other.CycleSequence
            && Proposal.Matches(other.Proposal)
            && string.Equals(LeaseId, other.LeaseId, StringComparison.Ordinal)
            && string.Equals(
                ProcessEpochId,
                other.ProcessEpochId,
                StringComparison.Ordinal);
}

internal sealed class AgentRuntimeHostSnapshot
{
    internal string ProcessEpochId { get; }
    internal long CycleSequence { get; }
    internal bool CycleOutputClaimed { get; }
    internal AgentLanguageSourceKind? ActiveSourceKind { get; }
    internal long Arbitrations { get; }
    internal long Publications { get; }
    internal long FailedPublications { get; }
    internal long RejectedClaims { get; }
    internal AgentLanguageSourceKind? LastLoserSourceKind { get; }
    internal AgentLanguageArbitrationReason LastLoserReason { get; }

    internal AgentRuntimeHostSnapshot(
        string processEpochId,
        long cycleSequence,
        bool cycleOutputClaimed,
        AgentLanguageSourceKind? activeSourceKind,
        long arbitrations,
        long publications,
        long failedPublications,
        long rejectedClaims,
        AgentLanguageSourceKind? lastLoserSourceKind,
        AgentLanguageArbitrationReason lastLoserReason)
    {
        ProcessEpochId = processEpochId;
        CycleSequence = cycleSequence;
        CycleOutputClaimed = cycleOutputClaimed;
        ActiveSourceKind = activeSourceKind;
        Arbitrations = arbitrations;
        Publications = publications;
        FailedPublications = failedPublications;
        RejectedClaims = rejectedClaims;
        LastLoserSourceKind = lastLoserSourceKind;
        LastLoserReason = lastLoserReason;
    }
}

internal sealed class AgentRuntimeHost
{
    internal const int MaximumProposalsPerArbitration = 16;

    private readonly object _gate = new();
    private readonly Func<long> _timestampProvider;
    private readonly long _timestampFrequency;
    private readonly string _processEpochId;
    private readonly object _issuerToken = new();
    private long _lastTimestamp = -1;
    private long _sequence;
    private long _cycleSequence = 1;
    private bool _cycleOutputClaimed;
    private AgentLanguagePublicationLease? _activeLease;
    private long _arbitrations;
    private long _publications;
    private long _failedPublications;
    private long _rejectedClaims;
    private AgentLanguageSourceKind? _lastLoserSourceKind;
    private AgentLanguageArbitrationReason _lastLoserReason;
    private AgentLanguageArbitrationOutcome? _latestOutcome;
    private AgentLanguageProposal? _currentCycleWinner;

    internal AgentRuntimeHost(
        Func<long>? timestampProvider = null,
        long? timestampFrequency = null,
        string? processEpochId = null)
    {
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (_timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        _processEpochId = AgentRuntimeGuard.Metadata(
            processEpochId
                ?? "agent-runtime-epoch-" + Guid.NewGuid().ToString("N"),
            nameof(processEpochId));
    }

    internal string ProcessEpochId => _processEpochId;

    internal bool BeginCycle()
    {
        lock (_gate)
        {
            if (_activeLease != null || !TryNow(out _))
                return false;
            if (_cycleSequence == long.MaxValue)
                return false;
            _cycleSequence++;
            _cycleOutputClaimed = false;
            _latestOutcome = null;
            _currentCycleWinner = null;
            return true;
        }
    }

    internal AgentRuntimeDeadline CreateDeadline(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        lock (_gate)
        {
            if (!TryNow(out var now))
                throw new InvalidOperationException(
                    "The process monotonic clock was rejected.");
            var sequence = NextSequence();
            var due = AddDuration(now, delay);
            return new AgentRuntimeDeadline(
                "agent-deadline-" + sequence.ToString("x") + "-"
                    + EpochSuffix(),
                _processEpochId,
                sequence,
                due,
                _issuerToken);
        }
    }

    internal bool IsDue(AgentRuntimeDeadline deadline)
    {
        if (deadline == null)
            throw new ArgumentNullException(nameof(deadline));
        lock (_gate)
        {
            return string.Equals(
                    deadline.ProcessEpochId,
                    _processEpochId,
                    StringComparison.Ordinal)
                && deadline.IsIssuedBy(_issuerToken)
                && TryNow(out var now)
                && now >= deadline.DueTimestamp;
        }
    }

    internal AgentLanguageProposal CreateLanguageProposal(
        AgentLanguageSourceKind sourceKind,
        long runtimeGeneration,
        string sourceCorrelationId,
        TimeSpan? notBeforeDelay = null,
        TimeSpan? lifetime = null)
    {
        if (!Enum.IsDefined(typeof(AgentLanguageSourceKind), sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        if (notBeforeDelay.HasValue
            && notBeforeDelay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(notBeforeDelay));
        }
        if (lifetime.HasValue && lifetime.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        lock (_gate)
        {
            if (!TryNow(out var now))
                throw new InvalidOperationException(
                    "The process monotonic clock was rejected.");
            var sequence = NextSequence();
            var notBefore = AddDuration(
                now,
                notBeforeDelay ?? TimeSpan.Zero);
            var expires = lifetime.HasValue
                ? AddDuration(notBefore, lifetime.Value)
                : (long?)null;
            return new AgentLanguageProposal(
                "agent-language-" + sequence.ToString("x") + "-"
                    + EpochSuffix(),
                _processEpochId,
                sequence,
                runtimeGeneration,
                sourceKind,
                sourceCorrelationId,
                now,
                notBefore,
                expires,
                _issuerToken);
        }
    }

    internal AgentLanguageArbitrationOutcome ArbitrateLanguage(
        IEnumerable<AgentLanguageProposal> proposals,
        long currentRuntimeGeneration,
        bool playerTurnActive,
        bool playerInputPending,
        bool dialogueBusy)
    {
        if (proposals == null)
            throw new ArgumentNullException(nameof(proposals));
        if (currentRuntimeGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRuntimeGeneration));
        }
        var proposalArray = proposals.ToArray();
        if (proposalArray.Length == 0
            || proposalArray.Length > MaximumProposalsPerArbitration
            || proposalArray.Any(item => item == null))
        {
            throw new ArgumentOutOfRangeException(nameof(proposals));
        }

        lock (_gate)
        {
            var timestampAccepted = TryNow(out var now);
            var reasons = new Dictionary<string, AgentLanguageArbitrationReason>(
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var proposal in proposalArray)
            {
                var reason = AgentLanguageArbitrationReason.None;
                if (!seen.Add(proposal.ProposalId))
                {
                    reason = AgentLanguageArbitrationReason.DuplicateProposal;
                }
                else if (!timestampAccepted)
                {
                    reason = AgentLanguageArbitrationReason
                        .MonotonicClockRejected;
                }
                else if (!string.Equals(
                    proposal.ProcessEpochId,
                    _processEpochId,
                    StringComparison.Ordinal))
                {
                    reason = AgentLanguageArbitrationReason.RuntimeEpochChanged;
                }
                else if (!proposal.IsIssuedBy(_issuerToken))
                {
                    reason = AgentLanguageArbitrationReason.ForeignProposal;
                }
                else if (proposal.IsClaimed)
                {
                    reason = AgentLanguageArbitrationReason.DuplicateProposal;
                }
                else if (proposal.RuntimeGeneration
                    != currentRuntimeGeneration)
                {
                    reason = AgentLanguageArbitrationReason
                        .RuntimeGenerationChanged;
                }
                else if (now < proposal.NotBeforeTimestamp)
                {
                    reason = AgentLanguageArbitrationReason.NotReady;
                }
                else if (proposal.ExpiresTimestamp.HasValue
                    && now >= proposal.ExpiresTimestamp.Value)
                {
                    reason = AgentLanguageArbitrationReason.Expired;
                }
                reasons[proposal.ProposalId] = reason;
            }

            var eligible = proposalArray
                .Where(item => reasons[item.ProposalId]
                    == AgentLanguageArbitrationReason.None)
                .ToArray();
            AgentLanguageProposal? selected = null;
            if (_cycleOutputClaimed)
            {
                foreach (var proposal in eligible)
                {
                    reasons[proposal.ProposalId] =
                        AgentLanguageArbitrationReason.EarlierOutputSelected;
                }
            }
            else
            {
                var ordinary = eligible
                    .Where(item => item.SourceKind
                        == AgentLanguageSourceKind.OrdinaryPlayerTurn)
                    .OrderBy(item => item.Sequence)
                    .FirstOrDefault();
                if (ordinary != null)
                {
                    selected = ordinary;
                }
                else if (playerInputPending)
                {
                    foreach (var proposal in eligible)
                    {
                        reasons[proposal.ProposalId] =
                            AgentLanguageArbitrationReason.PlayerInputPending;
                    }
                }
                else if (playerTurnActive)
                {
                    foreach (var proposal in eligible)
                    {
                        reasons[proposal.ProposalId] =
                            AgentLanguageArbitrationReason.ActivePlayerTurn;
                    }
                }
                else if (dialogueBusy)
                {
                    foreach (var proposal in eligible)
                    {
                        reasons[proposal.ProposalId] =
                            AgentLanguageArbitrationReason.DialogueBusy;
                    }
                }
                else
                {
                    selected = eligible
                        .OrderBy(item => Priority(item.SourceKind))
                        .ThenBy(item => item.Sequence)
                        .FirstOrDefault();
                }
            }

            if (selected != null)
            {
                foreach (var proposal in eligible)
                {
                    if (!ReferenceEquals(proposal, selected))
                    {
                        reasons[proposal.ProposalId] =
                            Priority(proposal.SourceKind)
                                    == Priority(selected.SourceKind)
                                ? AgentLanguageArbitrationReason
                                    .EarlierProposal
                                : AgentLanguageArbitrationReason
                                    .HigherPriorityProposal;
                    }
                }
            }

            if (!_cycleOutputClaimed
                && _currentCycleWinner != null
                && _currentCycleWinner.SourceKind
                    != AgentLanguageSourceKind.OrdinaryPlayerTurn
                && (playerInputPending || playerTurnActive || dialogueBusy))
            {
                _lastLoserSourceKind = _currentCycleWinner.SourceKind;
                _lastLoserReason = playerInputPending
                    ? AgentLanguageArbitrationReason.PlayerInputPending
                    : playerTurnActive
                        ? AgentLanguageArbitrationReason.ActivePlayerTurn
                        : AgentLanguageArbitrationReason.DialogueBusy;
                _currentCycleWinner = null;
                _latestOutcome = null;
            }

            var replacesCurrentCycleWinner = false;
            if (!_cycleOutputClaimed && _currentCycleWinner != null)
            {
                if (selected == null
                    || ComparePriority(
                        _currentCycleWinner,
                        selected) <= 0)
                {
                    foreach (var proposal in eligible)
                    {
                        reasons[proposal.ProposalId] =
                            Priority(proposal.SourceKind)
                                    == Priority(
                                        _currentCycleWinner.SourceKind)
                                ? AgentLanguageArbitrationReason
                                    .EarlierProposal
                                : AgentLanguageArbitrationReason
                                    .HigherPriorityProposal;
                    }
                    selected = null;
                }
                else
                {
                    replacesCurrentCycleWinner = true;
                }
            }
            else if (selected != null)
            {
                replacesCurrentCycleWinner = true;
            }

            var winningProposalId = selected?.ProposalId
                ?? _currentCycleWinner?.ProposalId
                ?? string.Empty;
            var proposalOutcomes = proposalArray.Select(proposal =>
            {
                var reason = reasons[proposal.ProposalId];
                return new AgentLanguageProposalOutcome(
                    proposal,
                    selected != null && ReferenceEquals(proposal, selected)
                        ? AgentLanguageArbitrationDisposition.Selected
                        : proposal.SourceKind
                                == AgentLanguageSourceKind.LocalTimer
                            && !IsTerminalReason(reason)
                                ? AgentLanguageArbitrationDisposition.Deferred
                                : AgentLanguageArbitrationDisposition.Rejected,
                    selected != null && ReferenceEquals(proposal, selected)
                        ? AgentLanguageArbitrationReason.None
                        : reason,
                    winningProposalId);
            }).ToArray();
            var outcomeSequence = NextSequence();
            var outcome = new AgentLanguageArbitrationOutcome(
                "agent-outcome-" + outcomeSequence.ToString("x") + "-"
                    + EpochSuffix(),
                _processEpochId,
                _cycleSequence,
                Math.Max(0, now),
                proposalOutcomes,
                selected);
            var lastLoser = proposalOutcomes.LastOrDefault(
                proposal => proposal.Disposition
                    != AgentLanguageArbitrationDisposition.Selected);
            if (lastLoser != null)
            {
                _lastLoserSourceKind = lastLoser.Proposal.SourceKind;
                _lastLoserReason = lastLoser.Reason;
            }
            if (replacesCurrentCycleWinner && selected != null)
            {
                _currentCycleWinner = selected;
                _latestOutcome = outcome;
            }
            _arbitrations++;
            return outcome;
        }
    }

    internal bool TryClaimLanguagePublication(
        AgentLanguageArbitrationOutcome outcome,
        AgentLanguageProposal proposal,
        long currentRuntimeGeneration,
        out AgentLanguagePublicationLease? lease)
    {
        if (outcome == null)
            throw new ArgumentNullException(nameof(outcome));
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (currentRuntimeGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRuntimeGeneration));
        }
        lease = null;
        lock (_gate)
        {
            var timestampAccepted = TryNow(out var now);
            if (_activeLease != null
                || _cycleOutputClaimed
                || !timestampAccepted
                || !ReferenceEquals(_latestOutcome, outcome)
                || !ReferenceEquals(_currentCycleWinner, proposal)
                || outcome.CycleSequence != _cycleSequence
                || !string.Equals(
                    outcome.ProcessEpochId,
                    _processEpochId,
                    StringComparison.Ordinal)
                || outcome.SelectedProposal == null
                || !ReferenceEquals(outcome.SelectedProposal, proposal)
                || outcome.Find(proposal)?.Disposition
                    != AgentLanguageArbitrationDisposition.Selected
                || !proposal.IsIssuedBy(_issuerToken)
                || proposal.IsClaimed
                || proposal.RuntimeGeneration != currentRuntimeGeneration
                || now < proposal.NotBeforeTimestamp
                || (proposal.ExpiresTimestamp.HasValue
                    && now >= proposal.ExpiresTimestamp.Value))
            {
                _rejectedClaims++;
                return false;
            }
            var sequence = NextSequence();
            if (!proposal.TryMarkClaimed(_issuerToken))
            {
                _rejectedClaims++;
                return false;
            }
            lease = new AgentLanguagePublicationLease(
                "agent-lease-" + sequence.ToString("x") + "-"
                    + EpochSuffix(),
                _processEpochId,
                _cycleSequence,
                proposal);
            _activeLease = lease;
            _cycleOutputClaimed = true;
            return true;
        }
    }

    internal bool TryCompleteLanguagePublication(
        AgentLanguagePublicationLease lease,
        bool published)
    {
        if (lease == null)
            throw new ArgumentNullException(nameof(lease));
        lock (_gate)
        {
            if (!ReferenceEquals(_activeLease, lease))
            {
                _rejectedClaims++;
                return false;
            }
            _activeLease = null;
            if (published)
                _publications++;
            else
                _failedPublications++;
            return true;
        }
    }

    internal AgentRuntimeHostSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new AgentRuntimeHostSnapshot(
                _processEpochId,
                _cycleSequence,
                _cycleOutputClaimed,
                _activeLease?.Proposal.SourceKind,
                _arbitrations,
                _publications,
                _failedPublications,
                _rejectedClaims,
                _lastLoserSourceKind,
                _lastLoserReason);
        }
    }

    private bool TryNow(out long timestamp)
    {
        timestamp = 0;
        long candidate;
        try
        {
            candidate = _timestampProvider();
        }
        catch
        {
            return false;
        }
        if (candidate < 0 || candidate < _lastTimestamp)
            return false;
        timestamp = candidate;
        _lastTimestamp = candidate;
        return true;
    }

    private long AddDuration(long timestamp, TimeSpan duration)
    {
        var delta = duration.TotalSeconds * _timestampFrequency;
        if (double.IsNaN(delta)
            || double.IsInfinity(delta)
            || delta < 0
            || delta > long.MaxValue - timestamp)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        return checked(timestamp + (long)Math.Ceiling(delta));
    }

    private long NextSequence()
    {
        if (_sequence == long.MaxValue)
            throw new InvalidOperationException("Agent runtime sequence exhausted.");
        return ++_sequence;
    }

    private string EpochSuffix()
        => _processEpochId.Substring(
            Math.Max(0, _processEpochId.Length - 12));

    private static int Priority(AgentLanguageSourceKind sourceKind)
        => sourceKind switch
        {
            AgentLanguageSourceKind.OrdinaryPlayerTurn => 1,
            AgentLanguageSourceKind.LocalTimer => 3,
            _ => int.MaxValue
        };

    private static int ComparePriority(
        AgentLanguageProposal left,
        AgentLanguageProposal right)
    {
        var sourcePriority = Priority(left.SourceKind)
            .CompareTo(Priority(right.SourceKind));
        return sourcePriority != 0
            ? sourcePriority
            : left.Sequence.CompareTo(right.Sequence);
    }

    private static bool IsTerminalReason(
        AgentLanguageArbitrationReason reason)
        => reason is AgentLanguageArbitrationReason.RuntimeGenerationChanged
            or AgentLanguageArbitrationReason.RuntimeEpochChanged
            or AgentLanguageArbitrationReason.Expired
            or AgentLanguageArbitrationReason.DuplicateProposal
            or AgentLanguageArbitrationReason.MonotonicClockRejected
            or AgentLanguageArbitrationReason.ForeignProposal;
}

internal static class AgentRuntimeGuard
{
    internal static string Metadata(
        string? value,
        string parameterName,
        int maximumLength = 160)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length == 0
            || result.Length > maximumLength
            || result.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Agent runtime metadata must be bounded and control-free.",
                parameterName);
        }
        return result;
    }
}
