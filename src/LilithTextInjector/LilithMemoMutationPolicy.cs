using System;

namespace LilithTextInjector;

internal static class LilithMemoMutationPolicy
{
    internal static bool ExactUpdatedTimeMatches(
        string? actualUpdatedTime,
        string? expectedUpdatedTime)
        => expectedUpdatedTime is not null
            && string.Equals(
                actualUpdatedTime,
                expectedUpdatedTime,
                StringComparison.Ordinal);

    internal static bool HasReminderCountdownConflict(
        bool currentRemind,
        bool currentCountdown,
        bool? requestedRemind,
        bool? requestedCountdown)
        => (requestedRemind ?? currentRemind)
            && (requestedCountdown ?? currentCountdown);
}
