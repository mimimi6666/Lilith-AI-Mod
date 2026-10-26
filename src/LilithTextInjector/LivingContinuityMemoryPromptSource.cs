using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

// Shared exact-context authorization. The retired Memory Core packet producer
// is intentionally absent; existing composition and reply contracts retain this guard.
internal sealed class LivingMemoryWorkingContextAuthorization
{
    private sealed class AuthorizedTurn
    {
        internal string TurnId { get; }
        internal LivingPromptTurnRole Role { get; }
        internal string ContentHash { get; }

        internal AuthorizedTurn(MemoryWorkingOccurrence occurrence)
        {
            TurnId = occurrence.OccurrenceId.ToString("N");
            Role = occurrence.Role == MemoryWorkingRole.Assistant
                ? LivingPromptTurnRole.Assistant
                : LivingPromptTurnRole.User;
            ContentHash = occurrence.ContentHash;
        }
    }

    private readonly IReadOnlyList<AuthorizedTurn> _turns;

    internal string CompositionId { get; }
    internal string ProviderRouteId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal LivingMemoryWorkingContextAuthorization(
        string compositionId,
        string providerRouteId,
        CharacterRuntimeIdentity runtimeIdentity,
        IEnumerable<MemoryWorkingOccurrence> workingOccurrences,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        CompositionId = LivingContinuityContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        ProviderRouteId = LivingContinuityContractGuard.Identifier(
            providerRouteId,
            nameof(providerRouteId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        var occurrences = (workingOccurrences
                ?? throw new ArgumentNullException(
                    nameof(workingOccurrences)))
            .ToArray();
        if (occurrences.Length == 0
            || occurrences.Length > MemoryWorkingSetReducer.MaximumTurns
            || occurrences.Any(item => item.IsBoundaryControl))
        {
            throw new ArgumentException(
                "Working authorization requires a bounded admitted Memory projection.",
                nameof(workingOccurrences));
        }
        _turns = Array.AsReadOnly(
            occurrences.Select(item => new AuthorizedTurn(item)).ToArray());
        IssuedAt = LivingContinuityContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ExpiresAt = LivingContinuityContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= IssuedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Fingerprint = LivingPromptHash.Of(
            string.Join(
                "|",
                CompositionId,
                ProviderRouteId,
                RuntimeIdentity.RuntimeInstanceId,
                RuntimeIdentity.Generation.ToString(
                    CultureInfo.InvariantCulture),
                string.Join(
                    ",",
                    _turns.Select(
                        item => item.TurnId
                            + ":"
                            + (int)item.Role
                            + ":"
                            + item.ContentHash)),
                IssuedAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture),
                ExpiresAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture)));
    }

    internal bool Matches(
        string compositionId,
        string providerRouteId,
        CharacterRuntimeIdentity runtimeIdentity,
        IReadOnlyList<LivingPromptTurn> turns,
        DateTimeOffset at)
    {
        if (runtimeIdentity == null || turns == null)
            return false;
        if (!string.Equals(
                CompositionId,
                compositionId,
                StringComparison.Ordinal)
            || !string.Equals(
                ProviderRouteId,
                providerRouteId,
                StringComparison.Ordinal)
            || !RuntimeIdentity.Matches(runtimeIdentity)
            || at < IssuedAt
            || at >= ExpiresAt
            || turns.Count != _turns.Count)
        {
            return false;
        }
        for (var index = 0; index < turns.Count; index++)
        {
            var turn = turns[index];
            var expected = _turns[index];
            if (turn.Origin != LivingPromptTurnOrigin.MemoryWorkingSet
                || !string.Equals(
                    turn.TurnId,
                    expected.TurnId,
                    StringComparison.Ordinal)
                || turn.Role != expected.Role
                || !string.Equals(
                    turn.ContentHash,
                    expected.ContentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}
