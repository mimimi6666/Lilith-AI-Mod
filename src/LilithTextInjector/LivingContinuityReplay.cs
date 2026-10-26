using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum ContinuityReplayOperation
{
    Acquire = 0,
    Consume = 1,
    Retire = 2
}

internal sealed class ContinuityReplayStep
{
    internal ContinuityReplayOperation Operation { get; }
    internal ContinuityOwnershipRequest Request { get; }
    internal CharacterRuntimeIdentity ActiveRuntime { get; }
    internal Guid ActiveSituationId { get; }
    internal long ActiveSituationRevision { get; }
    internal ContinuityRetirementReason RetirementReason { get; }
    internal DateTimeOffset At { get; }

    internal ContinuityReplayStep(
        ContinuityReplayOperation operation,
        ContinuityOwnershipRequest request,
        CharacterRuntimeIdentity activeRuntime,
        Guid activeSituationId,
        long activeSituationRevision,
        ContinuityRetirementReason retirementReason,
        DateTimeOffset at)
    {
        if (!Enum.IsDefined(typeof(ContinuityReplayOperation), operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ActiveRuntime = activeRuntime
            ?? throw new ArgumentNullException(nameof(activeRuntime));
        if (activeSituationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(activeSituationId));
        if (activeSituationRevision <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(activeSituationRevision));
        if (!Enum.IsDefined(
                typeof(ContinuityRetirementReason),
                retirementReason))
        {
            throw new ArgumentOutOfRangeException(nameof(retirementReason));
        }
        Operation = operation;
        ActiveSituationId = activeSituationId;
        ActiveSituationRevision = activeSituationRevision;
        RetirementReason = retirementReason;
        At = LivingContinuityContractGuard.Timestamp(at, nameof(at));
    }
}

internal sealed class ContinuityReplayResult
{
    internal IReadOnlyList<ContinuityOwnershipRecord> Records { get; }
    internal LivingContinuityAuthorityCounters AuthorityCounters { get; }

    internal ContinuityReplayResult(
        IReadOnlyList<ContinuityOwnershipRecord> records,
        LivingContinuityAuthorityCounters authorityCounters)
    {
        if (records == null)
            throw new ArgumentNullException(nameof(records));
        var copy = new ContinuityOwnershipRecord[records.Count];
        for (var index = 0; index < records.Count; index++)
        {
            copy[index] = records[index]
                ?? throw new ArgumentException(
                    "Replay records cannot contain null.",
                    nameof(records));
        }
        Records = Array.AsReadOnly(copy);
        AuthorityCounters = authorityCounters;
    }
}

internal static class LivingContinuityReplay
{
    internal static ContinuityReplayResult Run(
        IReadOnlyList<ContinuityReplayStep>? steps)
    {
        var source = steps ?? Array.Empty<ContinuityReplayStep>();
        var registry = new ContinuityOwnershipRegistry();
        var records = new ContinuityOwnershipRecord[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            var step = source[index]
                ?? throw new ArgumentException(
                    "Replay steps cannot contain null.",
                    nameof(steps));
            records[index] = step.Operation switch
            {
                ContinuityReplayOperation.Acquire =>
                    registry.Acquire(
                        step.Request,
                        step.ActiveRuntime,
                        step.At),
                ContinuityReplayOperation.Consume =>
                    registry.Consume(
                        step.Request,
                        step.ActiveRuntime,
                        step.ActiveSituationId,
                        step.ActiveSituationRevision,
                        step.At),
                ContinuityReplayOperation.Retire =>
                    registry.Retire(
                        step.Request,
                        step.RetirementReason,
                        step.At),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(step.Operation))
            };
        }

        return new ContinuityReplayResult(
            records,
            registry.AuthorityCounters);
    }
}
