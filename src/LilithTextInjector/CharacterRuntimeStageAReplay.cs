using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal sealed class CharacterProjectionReplayRecord
{
    internal long Sequence { get; }
    internal CharacterProjectionAdmission Admission { get; }
    internal CharacterProjectionDomain AffectedDomain { get; }
    internal string? ProjectionFingerprint { get; }
    internal long? ProjectionRevision { get; }
    internal long? SituationRevision { get; }

    internal CharacterProjectionReplayRecord(
        long sequence,
        CharacterProjectionResult result)
    {
        Sequence = CharacterRuntimeContractGuard.Positive(
            sequence,
            nameof(sequence));
        Admission = result.Admission;
        AffectedDomain = result.AffectedDomain;
        ProjectionFingerprint =
            result.Projection?.ProjectionFingerprint;
        ProjectionRevision = result.Projection?.ProjectionRevision;
        SituationRevision = result.Projection?.SituationRevision;
    }
}

internal sealed class CharacterRuntimeStageAReplayResult
{
    internal IReadOnlyList<CharacterProjectionReplayRecord> Records {
        get;
    }
    internal CharacterRuntimeStageAAuthorityCounters AuthorityCounters {
        get;
    }

    internal CharacterRuntimeStageAReplayResult(
        IEnumerable<CharacterProjectionReplayRecord> records)
    {
        Records = (records
                ?? throw new ArgumentNullException(nameof(records)))
            .ToArray();
        AuthorityCounters = CharacterRuntimeStageAAuthorityCounters.Zero;
    }
}

internal static class CharacterRuntimeStageAReplay
{
    internal static CharacterRuntimeStageAReplayResult Run(
        IEnumerable<CharacterProjectionInput> inputs)
    {
        if (inputs == null)
            throw new ArgumentNullException(nameof(inputs));

        var records = new List<CharacterProjectionReplayRecord>();
        long sequence = 0;
        foreach (var input in inputs)
        {
            sequence++;
            records.Add(
                new CharacterProjectionReplayRecord(
                    sequence,
                    CharacterSituationProjector.Project(input)));
        }

        return new CharacterRuntimeStageAReplayResult(records);
    }
}
