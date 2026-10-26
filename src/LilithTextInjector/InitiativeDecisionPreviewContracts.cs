using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum InitiativePreviewBehaviorCategory
{
    None = 0,
    Acknowledge = 1,
    ConversationOffer = 2,
    MotionOpportunity = 3,
    RestOrWait = 4,
    ConsentQuestion = 5,
    EvidenceNeeded = 6
}

internal enum InitiativePreviewAttentionBand
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal enum InitiativePreviewInterruptionBand
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

[Flags]
internal enum InitiativePreviewEvidenceRequirement
{
    None = 0,
    Memory = 1 << 0,
    Reasoning = 1 << 1,
    CharacterState = 1 << 2,
    RelationshipState = 1 << 3,
    CapabilityState = 1 << 4
}

internal enum InitiativePreviewConsentRequirement
{
    None = 0,
    Notice = 1,
    ExplicitConsent = 2,
    Unavailable = 3
}

internal enum InitiativePreviewExecutionPermission
{
    DeniedByObserveBoundary = 0
}

internal enum InitiativePreviewRetirementReason
{
    Expired = 0,
    Superseded = 1,
    Cleared = 2,
    SessionRetired = 3
}

internal enum InitiativePreviewMutationResult
{
    Accepted = 0,
    Retired = 1,
    TimeAdvanced = 2,
    Cleared = 3,
    Duplicate = 4,
    ActiveCapacityReached = 5,
    RetiredCapacityReached = 6,
    NotActive = 7
}

internal sealed class InitiativeDecisionPreview
{
    internal const int MaximumPreviewIdLength = 48;
    internal const int MaximumCandidateIdSuffixLength = 24;
    internal static readonly TimeSpan MaximumLifetime =
        TimeSpan.FromMinutes(10);

    private const InitiativePreviewEvidenceRequirement AllEvidence =
        InitiativePreviewEvidenceRequirement.Memory
        | InitiativePreviewEvidenceRequirement.Reasoning
        | InitiativePreviewEvidenceRequirement.CharacterState
        | InitiativePreviewEvidenceRequirement.RelationshipState
        | InitiativePreviewEvidenceRequirement.CapabilityState;

    internal string PreviewId { get; }
    internal string CandidateIdSuffix { get; }
    internal InitiativeProducerSourceKind Source { get; }
    internal DecisionDisposition Disposition { get; }
    internal DecisionReason Reason { get; }
    internal InitiativePreviewBehaviorCategory BehaviorCategory { get; }
    internal InitiativePreviewInterruptionBand InterruptionBand { get; }
    internal InitiativePreviewAttentionBand AttentionBand { get; }
    internal InitiativePreviewEvidenceRequirement EvidenceRequirement { get; }
    internal InitiativePreviewConsentRequirement ConsentRequirement { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal InitiativePreviewExecutionPermission ExecutionPermission =>
        InitiativePreviewExecutionPermission.DeniedByObserveBoundary;

    internal InitiativeDecisionPreview(
        string previewId,
        string candidateIdSuffix,
        InitiativeProducerSourceKind source,
        DecisionDisposition disposition,
        DecisionReason reason,
        InitiativePreviewBehaviorCategory behaviorCategory,
        InitiativePreviewInterruptionBand interruptionBand,
        InitiativePreviewAttentionBand attentionBand,
        InitiativePreviewEvidenceRequirement evidenceRequirement,
        InitiativePreviewConsentRequirement consentRequirement,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        PreviewId = OpaqueAsciiIdentifier(
            previewId,
            MaximumPreviewIdLength,
            nameof(previewId));
        CandidateIdSuffix = OpaqueAsciiIdentifier(
            candidateIdSuffix,
            MaximumCandidateIdSuffixLength,
            nameof(candidateIdSuffix));
        if (!Enum.IsDefined(typeof(InitiativeProducerSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(typeof(DecisionDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!Enum.IsDefined(typeof(DecisionReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (!Enum.IsDefined(
                typeof(InitiativePreviewBehaviorCategory),
                behaviorCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(behaviorCategory));
        }
        if (!Enum.IsDefined(
                typeof(InitiativePreviewInterruptionBand),
                interruptionBand))
        {
            throw new ArgumentOutOfRangeException(nameof(interruptionBand));
        }
        if (!Enum.IsDefined(
                typeof(InitiativePreviewAttentionBand),
                attentionBand))
        {
            throw new ArgumentOutOfRangeException(nameof(attentionBand));
        }
        if ((evidenceRequirement & ~AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(evidenceRequirement));
        if (!Enum.IsDefined(
                typeof(InitiativePreviewConsentRequirement),
                consentRequirement))
        {
            throw new ArgumentOutOfRangeException(nameof(consentRequirement));
        }

        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= ObservedAt
            || ExpiresAt - ObservedAt > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        Source = source;
        Disposition = disposition;
        Reason = reason;
        BehaviorCategory = behaviorCategory;
        InterruptionBand = interruptionBand;
        AttentionBand = attentionBand;
        EvidenceRequirement = evidenceRequirement;
        ConsentRequirement = consentRequirement;
    }

    internal static string OpaqueAsciiIdentifier(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "Opaque identifier cannot be empty.",
                parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        foreach (var character in normalized)
        {
            if (character < '!' || character > '~')
            {
                throw new ArgumentException(
                    "Opaque identifier must contain printable ASCII without spaces.",
                    parameterName);
            }
        }
        return normalized;
    }
}

internal sealed class InitiativeDecisionPreviewTombstone
{
    internal string PreviewId { get; }
    internal DateTimeOffset RetiredAt { get; }
    internal InitiativePreviewRetirementReason Reason { get; }

    internal InitiativeDecisionPreviewTombstone(
        string previewId,
        DateTimeOffset retiredAt,
        InitiativePreviewRetirementReason reason)
    {
        PreviewId = InitiativeDecisionPreview.OpaqueAsciiIdentifier(
            previewId,
            InitiativeDecisionPreview.MaximumPreviewIdLength,
            nameof(previewId));
        RetiredAt = DecisionCoreContractGuard.Timestamp(
            retiredAt,
            nameof(retiredAt));
        if (!Enum.IsDefined(
                typeof(InitiativePreviewRetirementReason),
                reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        Reason = reason;
    }
}

internal sealed class InitiativeDecisionPreviewLedger
{
    internal const int MaximumActivePreviews = 16;
    internal const int MaximumRetiredTombstones = 64;

    private readonly List<InitiativeDecisionPreview> _active = new();
    private readonly List<InitiativeDecisionPreviewTombstone> _retired =
        new();

    internal IReadOnlyList<InitiativeDecisionPreview> ActivePreviews =>
        Array.AsReadOnly(_active.ToArray());
    internal IReadOnlyList<InitiativeDecisionPreviewTombstone>
        RetiredTombstones => Array.AsReadOnly(_retired.ToArray());

    internal InitiativePreviewMutationResult TryAdd(
        InitiativeDecisionPreview preview)
    {
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        if (Contains(preview.PreviewId))
            return InitiativePreviewMutationResult.Duplicate;
        if (_active.Count >= MaximumActivePreviews)
        {
            return InitiativePreviewMutationResult
                .ActiveCapacityReached;
        }

        _active.Add(preview);
        return InitiativePreviewMutationResult.Accepted;
    }

    internal InitiativePreviewMutationResult TryRetire(
        string previewId,
        DateTimeOffset retiredAt,
        InitiativePreviewRetirementReason reason)
    {
        var normalized = InitiativeDecisionPreview.OpaqueAsciiIdentifier(
            previewId,
            InitiativeDecisionPreview.MaximumPreviewIdLength,
            nameof(previewId));
        var timestamp = DecisionCoreContractGuard.Timestamp(
            retiredAt,
            nameof(retiredAt));
        if (!Enum.IsDefined(
                typeof(InitiativePreviewRetirementReason),
                reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        var index = IndexOfActive(normalized);
        if (index < 0)
            return InitiativePreviewMutationResult.NotActive;
        if (_retired.Count >= MaximumRetiredTombstones)
        {
            return InitiativePreviewMutationResult
                .RetiredCapacityReached;
        }
        if (timestamp < _active[index].ObservedAt)
            throw new ArgumentOutOfRangeException(nameof(retiredAt));

        _active.RemoveAt(index);
        _retired.Add(new InitiativeDecisionPreviewTombstone(
            normalized,
            timestamp,
            reason));
        return InitiativePreviewMutationResult.Retired;
    }

    internal InitiativePreviewMutationResult TryAdvance(
        DateTimeOffset asOf)
    {
        var timestamp = DecisionCoreContractGuard.Timestamp(
            asOf,
            nameof(asOf));
        var expiredCount = 0;
        for (var index = 0; index < _active.Count; index++)
        {
            if (_active[index].ExpiresAt <= timestamp)
                expiredCount++;
        }
        if (_retired.Count + expiredCount
            > MaximumRetiredTombstones)
        {
            return InitiativePreviewMutationResult
                .RetiredCapacityReached;
        }

        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var preview = _active[index];
            if (preview.ExpiresAt > timestamp)
                continue;
            _active.RemoveAt(index);
            _retired.Add(new InitiativeDecisionPreviewTombstone(
                preview.PreviewId,
                timestamp,
                InitiativePreviewRetirementReason.Expired));
        }
        return InitiativePreviewMutationResult.TimeAdvanced;
    }

    internal InitiativePreviewMutationResult Clear()
    {
        _active.Clear();
        _retired.Clear();
        return InitiativePreviewMutationResult.Cleared;
    }

    private bool Contains(string previewId)
    {
        if (IndexOfActive(previewId) >= 0)
            return true;
        for (var index = 0; index < _retired.Count; index++)
        {
            if (string.Equals(
                    _retired[index].PreviewId,
                    previewId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private int IndexOfActive(string previewId)
    {
        for (var index = 0; index < _active.Count; index++)
        {
            if (string.Equals(
                    _active[index].PreviewId,
                    previewId,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }
}
