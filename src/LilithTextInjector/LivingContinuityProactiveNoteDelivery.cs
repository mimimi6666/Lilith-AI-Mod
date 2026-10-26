using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal sealed class LivingProactiveNoteFinalDeliveryFacts
{
    internal bool ExactActiveOperation { get; }
    internal bool MasterEnabled { get; }
    internal bool SourceEnabled { get; }
    internal bool ConfigurationMatches { get; }
    internal bool RuntimeMatches { get; }
    internal bool EpochMatches { get; }
    internal bool SituationMatches { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }
    internal bool IsCancelled { get; }
    internal bool IsBeforeCompletionDeadline { get; }

    internal LivingProactiveNoteFinalDeliveryFacts(
        bool exactActiveOperation,
        bool masterEnabled,
        bool sourceEnabled,
        bool configurationMatches,
        bool runtimeMatches,
        bool epochMatches,
        bool situationMatches,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool isCancelled,
        bool isBeforeCompletionDeadline)
    {
        ExactActiveOperation = exactActiveOperation;
        MasterEnabled = masterEnabled;
        SourceEnabled = sourceEnabled;
        ConfigurationMatches = configurationMatches;
        RuntimeMatches = runtimeMatches;
        EpochMatches = epochMatches;
        SituationMatches = situationMatches;
        IsAvatarUnderDirectPlayerControl =
            isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
        IsCancelled = isCancelled;
        IsBeforeCompletionDeadline = isBeforeCompletionDeadline;
    }
}

internal static class LivingProactiveNoteDeliveryPolicy
{
    internal static readonly TimeSpan SuccessfulHistoryIdentityTolerance =
        TimeSpan.FromSeconds(5);
    internal const string OwnedPendingStatus = "continuity-owned";
    internal const string LegacyGeneratingStatus = "generating";
    internal const string PendingStatus = "pending";
    internal const string CancelledStatus = "cancelled";
    internal const string DeliveredStatus = "delivered";

    internal static bool MayCommitNativeWrite(
        LivingProactiveNoteFinalDeliveryFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));
        return facts.ExactActiveOperation
            && facts.MasterEnabled
            && facts.SourceEnabled
            && facts.ConfigurationMatches
            && facts.RuntimeMatches
            && facts.EpochMatches
            && facts.SituationMatches
            && !facts.IsAvatarUnderDirectPlayerControl
            && !facts.IsConversationBusy
            && !facts.HasPendingExecution
            && !facts.IsQuietPeriod
            && !facts.IsCancelled
            && facts.IsBeforeCompletionDeadline;
    }

    internal static string RecoverStatus(
        bool livingContinuityOwned,
        string? persistedStatus)
    {
        var status = persistedStatus ?? string.Empty;
        if (livingContinuityOwned
            && (string.Equals(
                    status,
                    OwnedPendingStatus,
                    StringComparison.Ordinal)
                || string.Equals(
                    status,
                    LegacyGeneratingStatus,
                    StringComparison.Ordinal)))
        {
            return CancelledStatus;
        }
        if (!livingContinuityOwned
            && string.Equals(
                status,
                LegacyGeneratingStatus,
                StringComparison.Ordinal))
        {
            return PendingStatus;
        }
        return status;
    }

    // The durable native file is the externally visible effect. Notification
    // is a best-effort UI refresh and cannot turn an already-written file into
    // a failed outcome that might be generated again.
    internal static bool NativeWriteIsCompleted(bool durableFileWritten)
        => durableFileWritten;

    internal static IReadOnlyList<DateTimeOffset>
        CoalesceSuccessfulHistory(
            IEnumerable<DateTimeOffset> source,
            int maximumCount)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        var ordered = source
            .Where(value => value != default)
            .OrderBy(value => value)
            .ToArray();
        var result = new List<DateTimeOffset>(
            Math.Min(ordered.Length, maximumCount));
        foreach (var value in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(value);
                continue;
            }
            var previous = result[^1];
            if (value - previous <= SuccessfulHistoryIdentityTolerance)
            {
                // The latest timestamp is the conservative cooldown anchor.
                // State, runtime and native-file observations inside this
                // interval describe the same single external effect.
                result[^1] = value;
                continue;
            }
            result.Add(value);
        }
        if (result.Count > maximumCount)
        {
            result.RemoveRange(0, result.Count - maximumCount);
        }
        return result;
    }
}
