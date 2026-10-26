using System;

namespace LilithTextInjector;

internal sealed class NoEvidenceBaselineOutcome
{
    internal const int MaximumIdentifierLength = 128;

    internal string BaselineId { get; }
    internal string CandidateId { get; }
    internal string CandidateFingerprint { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal Guid DecisionId { get; }
    internal DecisionDisposition Disposition { get; }
    internal DecisionReason Reason { get; }
    internal string? SelectedGoalId { get; }
    internal DateTimeOffset EvaluatedAt { get; }

    internal NoEvidenceBaselineOutcome(
        string baselineId,
        string candidateId,
        string candidateFingerprint,
        Guid snapshotId,
        long situationRevision,
        Guid decisionId,
        DecisionDisposition disposition,
        DecisionReason reason,
        string? selectedGoalId,
        DateTimeOffset evaluatedAt)
    {
        BaselineId = Identifier(baselineId, nameof(baselineId));
        CandidateId = Identifier(candidateId, nameof(candidateId));
        CandidateFingerprint = Identifier(
            candidateFingerprint,
            nameof(candidateFingerprint));
        if (snapshotId == Guid.Empty)
        {
            throw new ArgumentException(
                "Snapshot ID cannot be empty.",
                nameof(snapshotId));
        }
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        if (decisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Decision ID cannot be empty.",
                nameof(decisionId));
        }
        if (!Enum.IsDefined(typeof(DecisionDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!Enum.IsDefined(typeof(DecisionReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (selectedGoalId != null)
        {
            SelectedGoalId = Identifier(
                selectedGoalId,
                nameof(selectedGoalId));
        }
        if (disposition is DecisionDisposition.Respond
                or DecisionDisposition.Act
                or DecisionDisposition.AskConsent
            && SelectedGoalId == null)
        {
            throw new ArgumentException(
                "Intervention baseline must reference its existing goal.",
                nameof(selectedGoalId));
        }

        SnapshotId = snapshotId;
        SituationRevision = situationRevision;
        DecisionId = decisionId;
        Disposition = disposition;
        Reason = reason;
        EvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
    }

    private static string Identifier(string? value, string parameterName)
        => DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);
}
