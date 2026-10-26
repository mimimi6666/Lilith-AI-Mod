using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal sealed class CharacterCanonProfileReplayInput
{
    internal CharacterCanonProfileIdentity Identity { get; }
    internal CharacterCanonProfileContent Content { get; }

    internal CharacterCanonProfileReplayInput(
        CharacterCanonProfileIdentity identity,
        CharacterCanonProfileContent content)
    {
        Identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
        Content = content
            ?? throw new ArgumentNullException(nameof(content));
    }
}

internal sealed class CharacterCanonProfileReplayRecord
{
    internal long Sequence { get; }
    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal int SchemaVersion { get; }
    internal CharacterCanonProfileAdmission Admission { get; }
    internal string? ComputedSemanticHash { get; }
    internal string? RejectedEvidenceId { get; }

    internal CharacterCanonProfileReplayRecord(
        long sequence,
        CharacterCanonProfileReplayInput input,
        CharacterCanonProfileValidationResult result)
    {
        Sequence = CharacterRuntimeContractGuard.Positive(
            sequence,
            nameof(sequence));
        ProfileId = input.Identity.ProfileId;
        ProfileVersion = input.Identity.ProfileVersion;
        SchemaVersion = input.Identity.SchemaVersion;
        Admission = result.Admission;
        ComputedSemanticHash = result.ComputedSemanticHash;
        RejectedEvidenceId = result.RejectedEvidenceId;
    }
}

internal sealed class CharacterCanonProfileReplayResult
{
    internal IReadOnlyList<CharacterCanonProfileReplayRecord> Records {
        get;
    }
    internal CharacterCanonProfileAuthorityCounters AuthorityCounters {
        get;
    }

    internal CharacterCanonProfileReplayResult(
        IEnumerable<CharacterCanonProfileReplayRecord> records)
    {
        Records = Array.AsReadOnly((records
                ?? throw new ArgumentNullException(nameof(records)))
            .ToArray());
        AuthorityCounters = CharacterCanonProfileAuthorityCounters.Zero;
    }
}

internal static class CharacterCanonProfileReplay
{
    internal static CharacterCanonProfileReplayResult Run(
        IEnumerable<CharacterCanonProfileReplayInput> inputs)
    {
        if (inputs == null)
            throw new ArgumentNullException(nameof(inputs));

        var records = new List<CharacterCanonProfileReplayRecord>();
        long sequence = 0;
        foreach (var input in inputs)
        {
            if (input == null)
                throw new ArgumentException(
                    "Replay input cannot contain null.",
                    nameof(inputs));
            sequence++;
            records.Add(
                new CharacterCanonProfileReplayRecord(
                    sequence,
                    input,
                    CharacterCanonProfileValidator.Validate(
                        input.Identity,
                        input.Content)));
        }

        return new CharacterCanonProfileReplayResult(records);
    }
}
