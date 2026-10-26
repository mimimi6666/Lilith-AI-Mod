using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum D022SliceBGateDisposition
{
    LegacyPassThroughOff = 0,
    NonAllowlistedMotionUnchanged = 1,
    LegacyOwnershipRecorded = 2,
    AssistOwnershipRecorded = 3,
    ExistingOwnershipReused = 4,
    RejectedAndRetired = 5
}

internal enum D022SliceBRetirementReason
{
    None = 0,
    RuntimeIdentityMismatch = 1,
    RuntimeGenerationMismatch = 2,
    AssistCapacityReached = 3
}

internal sealed class D022SliceBOpportunityContext
{
    internal string OccurrenceId { get; }
    internal long SourceSequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }

    internal D022SliceBOpportunityContext(
        string occurrenceId,
        long sourceSequence,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        string sessionId,
        string epochId,
        CharacterRuntimeIdentity runtimeIdentity)
    {
        OccurrenceId = D022ContractGuard.Identifier(
            occurrenceId,
            nameof(occurrenceId));
        if (sourceSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSequence));
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ObservedAt < OccurredAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        if (ExpiresAt <= ObservedAt
            || ExpiresAt - ObservedAt
                > D022SliceACeilings.CandidateLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        SessionId = D022ContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        EpochId = D022ContractGuard.Identifier(
            epochId,
            nameof(epochId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!string.Equals(
                RuntimeIdentity.OrchestratorSessionId,
                SessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Runtime identity and occurrence must use the same session.",
                nameof(runtimeIdentity));
        }

        SourceSequence = sourceSequence;
    }
}

internal sealed class D022SliceBOwnershipRecord
{
    internal MotionOpportunityIdentity Opportunity { get; }
    internal D022OpportunityOwnership Ownership { get; }
    internal D022SliceBRetirementReason RetirementReason { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity =>
        Opportunity.RuntimeIdentity;
    internal bool AllowsLegacyDirectSubmission =>
        Ownership == D022OpportunityOwnership.LegacyOwned;

    internal D022SliceBOwnershipRecord(
        MotionOpportunityIdentity opportunity,
        D022OpportunityOwnership ownership,
        D022SliceBRetirementReason retirementReason)
    {
        Opportunity = opportunity
            ?? throw new ArgumentNullException(nameof(opportunity));
        if (!Enum.IsDefined(typeof(D022OpportunityOwnership), ownership))
            throw new ArgumentOutOfRangeException(nameof(ownership));
        if (!Enum.IsDefined(
                typeof(D022SliceBRetirementReason),
                retirementReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retirementReason));
        }
        if (ownership == D022OpportunityOwnership.Retired
            && retirementReason == D022SliceBRetirementReason.None)
        {
            throw new ArgumentException(
                "A retired opportunity requires a typed reason.",
                nameof(retirementReason));
        }
        if (ownership != D022OpportunityOwnership.Retired
            && retirementReason != D022SliceBRetirementReason.None)
        {
            throw new ArgumentException(
                "Only retired opportunities may carry a retirement reason.",
                nameof(retirementReason));
        }

        Ownership = ownership;
        RetirementReason = retirementReason;
    }
}

internal sealed class D022SliceBGateResult
{
    private static readonly D022SliceBGateResult LegacyOff =
        new(
            D022SliceBGateDisposition.LegacyPassThroughOff,
            D022OpportunityOwnership.LegacyOwned,
            retirementReason: D022SliceBRetirementReason.None,
            opportunity: null,
            allowsLegacyDirectSubmission: true);

    private static readonly D022SliceBGateResult NonAllowlisted =
        new(
            D022SliceBGateDisposition.NonAllowlistedMotionUnchanged,
            ownership: null,
            retirementReason: D022SliceBRetirementReason.None,
            opportunity: null,
            allowsLegacyDirectSubmission: true);

    internal D022SliceBGateDisposition Disposition { get; }
    internal D022OpportunityOwnership? Ownership { get; }
    internal D022SliceBRetirementReason RetirementReason { get; }
    internal MotionOpportunityIdentity? Opportunity { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity =>
        Opportunity?.RuntimeIdentity;
    internal bool AllowsLegacyDirectSubmission { get; }
    internal D022SliceAExecutionPermission ExecutionPermission =>
        D022SliceAExecutionPermission.DeniedByContractOnlyBoundary;
    internal D022SliceASideEffectCounters AuthorityOutputs =>
        D022SliceASideEffectCounters.Zero;

    private D022SliceBGateResult(
        D022SliceBGateDisposition disposition,
        D022OpportunityOwnership? ownership,
        D022SliceBRetirementReason retirementReason,
        MotionOpportunityIdentity? opportunity,
        bool allowsLegacyDirectSubmission)
    {
        if (!Enum.IsDefined(typeof(D022SliceBGateDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (ownership.HasValue
            && !Enum.IsDefined(
                typeof(D022OpportunityOwnership),
                ownership.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }
        if (!Enum.IsDefined(
                typeof(D022SliceBRetirementReason),
                retirementReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retirementReason));
        }
        if (ownership == D022OpportunityOwnership.AssistOwned
            && allowsLegacyDirectSubmission)
        {
            throw new ArgumentException(
                "Assist ownership can never permit legacy fallback.",
                nameof(allowsLegacyDirectSubmission));
        }
        if (ownership == D022OpportunityOwnership.Retired
            && allowsLegacyDirectSubmission)
        {
            throw new ArgumentException(
                "A retired Assist-routed occurrence cannot fall back.",
                nameof(allowsLegacyDirectSubmission));
        }

        Disposition = disposition;
        Ownership = ownership;
        RetirementReason = retirementReason;
        Opportunity = opportunity;
        AllowsLegacyDirectSubmission = allowsLegacyDirectSubmission;
    }

    internal static D022SliceBGateResult DefaultOff(
        MotionDirectorProposal proposal)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        return IsAllowlistedOccurrence(proposal)
            ? LegacyOff
            : NonAllowlisted;
    }

    internal static D022SliceBGateResult FromRecord(
        D022SliceBOwnershipRecord record,
        bool existing)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        var disposition = existing
            ? D022SliceBGateDisposition.ExistingOwnershipReused
            : record.Ownership switch
            {
                D022OpportunityOwnership.LegacyOwned =>
                    D022SliceBGateDisposition.LegacyOwnershipRecorded,
                D022OpportunityOwnership.AssistOwned =>
                    D022SliceBGateDisposition.AssistOwnershipRecorded,
                D022OpportunityOwnership.Retired =>
                    D022SliceBGateDisposition.RejectedAndRetired,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(record))
            };
        return new D022SliceBGateResult(
            disposition,
            record.Ownership,
            record.RetirementReason,
            record.Opportunity,
            record.AllowsLegacyDirectSubmission);
    }

    internal static bool IsAllowlistedOccurrence(
        MotionDirectorProposal proposal)
        => proposal.Disposition == MotionDirectorDisposition.Dispatch
            && proposal.Kind == MotionIntentKind.PlayOneShot
            && proposal.Source == MotionIntentSource.Initiative
            && proposal.Reason == MotionDirectorReason.React;
}

internal sealed class D022PlayOneShotOwnershipGate
{
    private readonly object _gate = new();
    private readonly Dictionary<string, D022SliceBOwnershipRecord>
        _records = new(StringComparer.Ordinal);
    private string? _activeAssistOpportunityId;

    internal static D022SliceBGateResult EvaluateDefaultOff(
        MotionDirectorProposal proposal)
        => D022SliceBGateResult.DefaultOff(proposal);

    internal D022SliceBGateResult ClaimForDeterministicBoundary(
        MotionDirectorProposal proposal,
        D022SliceBOpportunityContext context,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        bool requestAssistOwnership)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (activeRuntimeIdentity == null)
        {
            throw new ArgumentNullException(
                nameof(activeRuntimeIdentity));
        }
        if (!D022SliceBGateResult.IsAllowlistedOccurrence(proposal))
            return D022SliceBGateResult.DefaultOff(proposal);
        lock (_gate)
        {
            if (_records.TryGetValue(
                    context.OccurrenceId,
                    out var existing))
            {
                return D022SliceBGateResult.FromRecord(
                    existing,
                    existing: true);
            }

            var opportunity = CreateOpportunity(proposal, context);
            var ownership = D022OpportunityOwnership.LegacyOwned;
            var retirementReason = D022SliceBRetirementReason.None;
            if (requestAssistOwnership)
            {
                var identityMatches =
                    context.RuntimeIdentity.Matches(activeRuntimeIdentity);
                if (!identityMatches)
                {
                    ownership = D022OpportunityOwnership.Retired;
                    retirementReason =
                        SameRuntimeWithoutGeneration(
                            context.RuntimeIdentity,
                            activeRuntimeIdentity)
                        ? D022SliceBRetirementReason
                            .RuntimeGenerationMismatch
                        : D022SliceBRetirementReason
                            .RuntimeIdentityMismatch;
                }
                else if (_activeAssistOpportunityId != null)
                {
                    ownership = D022OpportunityOwnership.Retired;
                    retirementReason =
                        D022SliceBRetirementReason.AssistCapacityReached;
                }
                else
                {
                    ownership = D022OpportunityOwnership.AssistOwned;
                    _activeAssistOpportunityId =
                        opportunity.OpportunityId;
                }
            }

            var record = new D022SliceBOwnershipRecord(
                opportunity,
                ownership,
                retirementReason);
            _records.Add(opportunity.OpportunityId, record);
            return D022SliceBGateResult.FromRecord(
                record,
                existing: false);
        }
    }

    internal int RecordCount
    {
        get
        {
            lock (_gate)
                return _records.Count;
        }
    }

    private static MotionOpportunityIdentity CreateOpportunity(
        MotionDirectorProposal proposal,
        D022SliceBOpportunityContext context)
        => new(
            context.OccurrenceId,
            proposal.ProviderId,
            context.SourceSequence,
            proposal.Kind,
            proposal.Reason,
            context.OccurredAt,
            context.ObservedAt,
            context.ExpiresAt,
            context.SessionId,
            context.EpochId,
            context.RuntimeIdentity);

    private static bool SameRuntimeWithoutGeneration(
        CharacterRuntimeIdentity left,
        CharacterRuntimeIdentity right)
        => string.Equals(
                left.RuntimeProfileId,
                right.RuntimeProfileId,
                StringComparison.Ordinal)
            && string.Equals(
                left.RuntimeInstanceId,
                right.RuntimeInstanceId,
                StringComparison.Ordinal)
            && string.Equals(
                left.OrchestratorSessionId,
                right.OrchestratorSessionId,
                StringComparison.Ordinal)
            && string.Equals(
                left.OwnerIdentity,
                right.OwnerIdentity,
                StringComparison.Ordinal);
}
