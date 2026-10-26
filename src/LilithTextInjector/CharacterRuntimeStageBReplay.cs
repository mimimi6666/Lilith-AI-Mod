using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class CharacterFederatedProjectionReplayRecord
{
    internal long Sequence { get; }
    internal CharacterProjectionAdmission Admission { get; }
    internal CharacterProjectionDomain AffectedDomain { get; }
    internal CharacterProjectionDomain MissingDomains { get; }
    internal CharacterProjectionDomain StaleDomains { get; }
    internal CharacterProjectionDomain AmbiguousDomains { get; }
    internal CharacterProjectionDomain ConflictingDomains { get; }
    internal string? ProjectionFingerprint { get; }
    internal string? SemanticProjectionFingerprint { get; }
    internal CharacterInfluenceMode InfluenceMode { get; }

    internal CharacterFederatedProjectionReplayRecord(
        long sequence,
        CharacterFederatedProjectionResult result)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        Sequence = sequence;
        Admission = result.Admission;
        AffectedDomain = result.ProjectionResult.AffectedDomain;
        MissingDomains = result.Diagnostics.MissingDomains;
        StaleDomains = result.Diagnostics.StaleDomains;
        AmbiguousDomains = result.Diagnostics.AmbiguousDomains;
        ConflictingDomains = result.Diagnostics.ConflictingDomains;
        ProjectionFingerprint = result.Projection?.ProjectionFingerprint;
        SemanticProjectionFingerprint =
            result.Projection?.SemanticProjectionFingerprint;
        InfluenceMode = result.InfluenceMode;
    }
}

internal sealed class CharacterRuntimeStageBReplayResult
{
    internal IReadOnlyList<CharacterFederatedProjectionReplayRecord>
        Records { get; }
    internal CharacterRuntimeStageAAuthorityCounters AuthorityCounters {
        get;
    }

    internal CharacterRuntimeStageBReplayResult(
        IReadOnlyList<CharacterFederatedProjectionReplayRecord> records)
    {
        Records = records ?? throw new ArgumentNullException(nameof(records));
        AuthorityCounters = CharacterRuntimeStageAAuthorityCounters.Zero;
    }
}

internal static class CharacterRuntimeStageBReplay
{
    internal static CharacterRuntimeStageBReplayResult Run(
        IEnumerable<CharacterFederatedProjectionInput> inputs)
    {
        if (inputs == null)
            throw new ArgumentNullException(nameof(inputs));

        var records = new List<CharacterFederatedProjectionReplayRecord>();
        long sequence = 0;
        foreach (var input in inputs)
        {
            sequence = checked(sequence + 1);
            records.Add(
                new CharacterFederatedProjectionReplayRecord(
                    sequence,
                    CharacterFederatedSituationProjector.Project(input)));
        }

        return new CharacterRuntimeStageBReplayResult(
            Array.AsReadOnly(records.ToArray()));
    }
}
