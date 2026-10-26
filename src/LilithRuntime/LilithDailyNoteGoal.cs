using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal enum LilithDailyNoteGoalPhase { Ready, EvaluationReserved, DeliveryReserved, Unknown, Satisfied, Exhausted, Expired }
internal enum LilithDailyNoteEffect { None, Evaluate, Deliver, Reconcile }
internal enum LilithDailyNoteEvidenceKind { DefiniteNoEffect, Receipt, Unknown, NotFound }

internal sealed record LilithDailyNoteAttempt(Guid GoalId, Guid AttemptId, Guid DeliveryId, Guid OperationId,
    DateOnly Day, int Ordinal, DateTimeOffset ReservedAtUtc, DateTimeOffset? DeliveryReservedAtUtc = null);

internal sealed record LilithDailyNoteGoalEntry(DateOnly Day, Guid GoalId, int Attempts,
    LilithDailyNoteGoalPhase Phase, DateTimeOffset? NotBeforeUtc, long LastOpportunitySequence,
    LilithDailyNoteAttempt? Attempt, string Reason);

// High-water marks cover committed transitions, not every read-only poll.
// The injected host clock attestation must independently cover cold starts.
internal sealed record LilithDailyNoteGoalSnapshot(int SchemaVersion, long Revision, string ProfileId,
    string TimeZoneId, string TimeZoneFingerprint, DateTimeOffset UtcHighWater, DateOnly DayHighWater,
    DateOnly? LastSatisfiedDay, Guid? LastSatisfiedDeliveryId, LilithDailyNoteGoalEntry? Current);

// ClockTrusted is supplied by the future host's wall/monotonic clock attestation.
// In particular, a forward wall-clock jump is not automatically trusted here.
internal sealed record LilithDailyNoteObservation(string ProfileId, TimeZoneInfo TimeZone,
    DateTimeOffset UtcNow, bool ClockTrusted, bool Enabled, bool ServiceReady,
    bool SafeOpportunity, long OpportunitySequence);

// DefiniteNoEffect is a trusted host proof, NOT an interpretation of Failed,
// Deferred, NotFound, a missing JSON file, or the model's own description.
internal sealed record LilithDailyNoteEvidence(LilithDailyNoteEvidenceKind Kind, LilithNoteInboxResult? Receipt = null);

// This is a proposal, not authority to execute. The durable store must commit
// Next against the exact predecessor before returning any non-None effect.
internal sealed record LilithDailyNoteTransition(LilithDailyNoteGoalSnapshot Next,
    LilithDailyNoteEffect Effect, LilithDailyNoteAttempt? Attempt, string Reason);

internal static class LilithDailyNoteGoal
{
    internal const int MaximumAttempts = 3;
    internal static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(30);

    internal static string Fingerprint(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(zone.ToSerializedString())));
    }

    internal static LilithDailyNoteGoalSnapshot Create(string profileId, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var state = new LilithDailyNoteGoalSnapshot(1, 0, profileId, zone.Id, Fingerprint(zone),
            DateTimeOffset.MinValue, DateOnly.MinValue, null, null, null);
        Validate(state);
        return state;
    }

    internal static LilithDailyNoteTransition TryBeginEvaluation(LilithDailyNoteGoalSnapshot state,
        LilithDailyNoteObservation observation)
    {
        var blocked = Guard(state, observation);
        if (blocked is not null) return None(state, blocked);
        var day = Day(observation);
        var current = state.Current;
        if (current is not null && Outstanding(current.Phase)) return None(state, "attempt_outstanding");
        if (state.LastSatisfiedDay == day) return None(state, "day_satisfied");
        if (!observation.Enabled) return None(state, "disabled");
        if (!observation.ServiceReady) return None(state, "service_unavailable");
        if (!observation.SafeOpportunity) return None(state, "unsafe_opportunity");
        // Midnight starts a new daily budget, not an exemption from a pending
        // no-effect cooldown (including an exhausted or expired prior goal).
        if (current?.NotBeforeUtc is { } notBefore && observation.UtcNow < notBefore) return None(state, "backoff");
        if (current is null || current.Day < day)
            current = new(day, GoalId(state, day), 0, LilithDailyNoteGoalPhase.Ready, null,
                current?.LastOpportunitySequence ?? 0, null, "new_day");
        if (current.Phase != LilithDailyNoteGoalPhase.Ready) return None(state, "day_closed");
        if (current.Attempts >= MaximumAttempts) return None(state, "attempts_exhausted");
        if (observation.OpportunitySequence <= current.LastOpportunitySequence) return None(state, "no_fresh_opportunity");
        var ordinal = current.Attempts + 1;
        var attempt = NewAttempt(current.GoalId, day, ordinal, observation.UtcNow);
        var next = current with { Attempts = ordinal, Phase = LilithDailyNoteGoalPhase.EvaluationReserved,
            NotBeforeUtc = null, LastOpportunitySequence = observation.OpportunitySequence, Attempt = attempt, Reason = "evaluation_reserved" };
        return Change(state, observation, next, LilithDailyNoteEffect.Evaluate, "evaluation_reserved");
    }

    internal static LilithDailyNoteTransition TryBeginDelivery(LilithDailyNoteGoalSnapshot state,
        LilithDailyNoteAttempt attempt, LilithDailyNoteObservation observation)
    {
        var blocked = Guard(state, observation);
        if (blocked is not null) return None(state, blocked);
        if (!Matches(state, attempt)) return None(state, "attempt_mismatch");
        var current = state.Current!;
        if (current.Phase != LilithDailyNoteGoalPhase.EvaluationReserved) return None(state, "delivery_already_reserved_or_closed");
        if (Day(observation) != current.Day)
            return Change(state, observation, current with { Phase = LilithDailyNoteGoalPhase.Expired, Reason = "expired_day" },
                LilithDailyNoteEffect.None, "expired_day");
        if (!observation.Enabled) return None(state, "disabled");
        if (!observation.ServiceReady) return None(state, "service_unavailable");
        if (!observation.SafeOpportunity) return None(state, "unsafe_opportunity");
        var reserved = attempt with { DeliveryReservedAtUtc = observation.UtcNow };
        return Change(state, observation, current with { Phase = LilithDailyNoteGoalPhase.DeliveryReserved,
            Attempt = reserved, Reason = "delivery_reserved" }, LilithDailyNoteEffect.Deliver, "delivery_reserved");
    }

    internal static LilithDailyNoteTransition ApplyEvidence(LilithDailyNoteGoalSnapshot state,
        LilithDailyNoteAttempt attempt, LilithDailyNoteEvidence evidence, LilithDailyNoteObservation observation)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var blocked = Guard(state, observation);
        if (blocked is not null) return None(state, blocked);
        if (!Matches(state, attempt)) return None(state, "attempt_mismatch");
        var current = state.Current!;
        if (!Outstanding(current.Phase)) return None(state, "terminal_or_inactive");
        if (!Enum.IsDefined(evidence.Kind)) return None(state, "invalid_evidence");
        if (evidence.Kind != LilithDailyNoteEvidenceKind.Receipt && evidence.Receipt is not null)
            return None(state, "invalid_evidence");
        if (evidence.Kind == LilithDailyNoteEvidenceKind.DefiniteNoEffect)
        {
            var phase = Day(observation) != current.Day ? LilithDailyNoteGoalPhase.Expired
                : current.Attempts >= MaximumAttempts ? LilithDailyNoteGoalPhase.Exhausted : LilithDailyNoteGoalPhase.Ready;
            return Change(state, observation, current with { Phase = phase,
                NotBeforeUtc = observation.UtcNow.Add(RetryBackoff),
                Reason = "verified_no_effect" }, LilithDailyNoteEffect.None, "verified_no_effect");
        }
        if (evidence.Kind == LilithDailyNoteEvidenceKind.Receipt)
        {
            var receipt = evidence.Receipt;
            if (receipt is null || receipt.DeliveryId != attempt.DeliveryId || receipt.OperationId != attempt.OperationId)
                return None(state, "receipt_mismatch");
            if (receipt.Status == LilithNoteInboxStatus.Delivered)
            {
                var artifact = receipt.Artifact;
                if (!attempt.DeliveryReservedAtUtc.HasValue || artifact is null || artifact.ArtifactId != attempt.DeliveryId
                    || artifact.CompletedAtUtc < attempt.DeliveryReservedAtUtc.Value || artifact.CompletedAtUtc > observation.UtcNow)
                    return None(state, "artifact_time_or_binding_invalid");
                var completedDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(artifact.CompletedAtUtc, observation.TimeZone).DateTime);
                if (completedDay < current.Day) return None(state, "artifact_day_invalid");
                var next = state with { Revision = checked(state.Revision + 1), UtcHighWater = observation.UtcNow,
                    DayHighWater = Day(observation), LastSatisfiedDay = completedDay, LastSatisfiedDeliveryId = artifact.ArtifactId,
                    Current = current with { Phase = LilithDailyNoteGoalPhase.Satisfied, NotBeforeUtc = null, Reason = "delivered" } };
                Validate(next);
                return new(next, LilithDailyNoteEffect.None, attempt, "delivered");
            }
            // None of the other public statuses is a no-write attestation.
        }
        if (current.Phase == LilithDailyNoteGoalPhase.Unknown) return None(state, "reconcile_only");
        return Change(state, observation, current with { Phase = LilithDailyNoteGoalPhase.Unknown, Reason = "reconcile_only" },
            LilithDailyNoteEffect.Reconcile, "reconcile_only");
    }

    internal static LilithDailyNoteTransition ResumeAfterRestart(LilithDailyNoteGoalSnapshot state,
        LilithDailyNoteObservation observation)
    {
        var blocked = Guard(state, observation);
        if (blocked is not null) return None(state, blocked);
        if (state.Current is not { } current || !Outstanding(current.Phase)) return None(state, "nothing_to_resume");
        // A reserved model attempt may have reached a tool before acknowledgement.
        // It consumes its original budget and is never silently issued again.
        return Change(state, observation, current with { Phase = LilithDailyNoteGoalPhase.Unknown, Reason = "restart_reconcile" },
            LilithDailyNoteEffect.Reconcile, "restart_reconcile");
    }

    internal static void Validate(LilithDailyNoteGoalSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != 1 || state.Revision < 0 || !Token(state.ProfileId, 128)
            || string.IsNullOrWhiteSpace(state.TimeZoneId) || state.TimeZoneId.Length > 256
            || state.TimeZoneId.Any(char.IsControl) || !Digest(state.TimeZoneFingerprint) || state.UtcHighWater.Offset != TimeSpan.Zero
            || state.LastSatisfiedDay.HasValue != state.LastSatisfiedDeliveryId.HasValue
            || state.LastSatisfiedDeliveryId == Guid.Empty || state.LastSatisfiedDay > state.DayHighWater)
            throw new ArgumentException("Daily note snapshot header rejected.");
        if (state.Current is not { } goal)
        {
            if (state.Revision != 0 || state.UtcHighWater != DateTimeOffset.MinValue
                || state.DayHighWater != DateOnly.MinValue || state.LastSatisfiedDay.HasValue)
                throw new ArgumentException("Daily note initial state rejected.");
            return;
        }
        if (state.Revision == 0 || !ObservationTime(state.UtcHighWater))
            throw new ArgumentException("Daily note committed clock rejected.");
        if (goal.Day > state.DayHighWater || goal.GoalId != GoalId(state, goal.Day)
            || goal.Attempts is < 1 or > MaximumAttempts || !Enum.IsDefined(goal.Phase)
            || goal.LastOpportunitySequence <= 0 || !Token(goal.Reason, 64)
            || goal.NotBeforeUtc.HasValue && !FiniteUtc(goal.NotBeforeUtc.Value)
            || goal.Attempt is null)
            throw new ArgumentException("Daily note goal rejected.");
        if (goal.Attempt is { } attempt)
        {
            var expected = NewAttempt(goal.GoalId, goal.Day, goal.Attempts, attempt.ReservedAtUtc);
            if (attempt.GoalId != expected.GoalId || attempt.AttemptId != expected.AttemptId
                || attempt.DeliveryId != expected.DeliveryId || attempt.OperationId != expected.OperationId
                || attempt.Ordinal != goal.Attempts || attempt.Day != goal.Day || !ObservationTime(attempt.ReservedAtUtc)
                || attempt.ReservedAtUtc > state.UtcHighWater
                || attempt.DeliveryReservedAtUtc.HasValue && (!ObservationTime(attempt.DeliveryReservedAtUtc.Value)
                    || attempt.DeliveryReservedAtUtc.Value < attempt.ReservedAtUtc || attempt.DeliveryReservedAtUtc.Value > state.UtcHighWater))
                throw new ArgumentException("Daily note attempt binding rejected.");
        }
        if (goal.Phase == LilithDailyNoteGoalPhase.EvaluationReserved && goal.Attempt!.DeliveryReservedAtUtc.HasValue
            || (goal.Phase is LilithDailyNoteGoalPhase.DeliveryReserved or LilithDailyNoteGoalPhase.Satisfied) && goal.Attempt?.DeliveryReservedAtUtc is null
            || goal.Phase == LilithDailyNoteGoalPhase.Satisfied && (state.LastSatisfiedDeliveryId != goal.Attempt!.DeliveryId
                || !state.LastSatisfiedDay.HasValue || state.LastSatisfiedDay.Value < goal.Day)
            || goal.Phase != LilithDailyNoteGoalPhase.Satisfied && state.LastSatisfiedDay >= goal.Day
            || goal.Phase == LilithDailyNoteGoalPhase.Expired && goal.Day >= state.DayHighWater
            || goal.Phase == LilithDailyNoteGoalPhase.Exhausted && goal.Attempts != MaximumAttempts
            || goal.Phase == LilithDailyNoteGoalPhase.Ready && goal.Attempts >= MaximumAttempts
            || (goal.Phase is LilithDailyNoteGoalPhase.Ready or LilithDailyNoteGoalPhase.Exhausted) && !goal.NotBeforeUtc.HasValue
            || goal.Phase == LilithDailyNoteGoalPhase.Expired && (goal.Reason == "verified_no_effect") != goal.NotBeforeUtc.HasValue
            || goal.NotBeforeUtc.HasValue && ((goal.Phase is not (LilithDailyNoteGoalPhase.Ready or LilithDailyNoteGoalPhase.Exhausted or LilithDailyNoteGoalPhase.Expired))
                || goal.NotBeforeUtc.Value < state.UtcHighWater.Add(RetryBackoff)))
            throw new ArgumentException("Daily note phase invariant rejected.");
    }

    private static string? Guard(LilithDailyNoteGoalSnapshot state, LilithDailyNoteObservation observation)
    {
        Validate(state);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ProfileId != state.ProfileId) return "profile_mismatch";
        if (observation.TimeZone is null || observation.TimeZone.Id != state.TimeZoneId
            || Fingerprint(observation.TimeZone) != state.TimeZoneFingerprint) return "time_zone_mismatch";
        if (!observation.ClockTrusted || !ObservationTime(observation.UtcNow)) return "clock_untrusted";
        if (observation.UtcNow < state.UtcHighWater || Day(observation) < state.DayHighWater) return "clock_rollback";
        if (state.Revision == long.MaxValue) return "revision_exhausted";
        if (observation.OpportunitySequence < 0) return "invalid_opportunity";
        return null;
    }

    private static LilithDailyNoteTransition Change(LilithDailyNoteGoalSnapshot state,
        LilithDailyNoteObservation observation, LilithDailyNoteGoalEntry current, LilithDailyNoteEffect effect, string reason)
    {
        var next = state with { Revision = checked(state.Revision + 1), UtcHighWater = observation.UtcNow,
            DayHighWater = Day(observation), Current = current };
        Validate(next);
        return new(next, effect, current.Attempt, reason);
    }

    private static LilithDailyNoteTransition None(LilithDailyNoteGoalSnapshot state, string reason)
        => new(state, LilithDailyNoteEffect.None, null, reason);
    private static bool Matches(LilithDailyNoteGoalSnapshot state, LilithDailyNoteAttempt attempt)
        => attempt is not null && state.Current?.Attempt == attempt;
    private static bool Outstanding(LilithDailyNoteGoalPhase phase)
        => phase is LilithDailyNoteGoalPhase.EvaluationReserved or LilithDailyNoteGoalPhase.DeliveryReserved or LilithDailyNoteGoalPhase.Unknown;
    private static bool FiniteUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero && value > DateTimeOffset.MinValue && value < DateTimeOffset.MaxValue;
    private static bool ObservationTime(DateTimeOffset value)
        => FiniteUtc(value) && value < DateTimeOffset.MaxValue.Subtract(RetryBackoff);
    private static DateOnly Day(LilithDailyNoteObservation observation)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(observation.UtcNow, observation.TimeZone).DateTime);
    private static Guid GoalId(LilithDailyNoteGoalSnapshot state, DateOnly day)
        => Id("goal", state.ProfileId, state.TimeZoneId, state.TimeZoneFingerprint, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    private static LilithDailyNoteAttempt NewAttempt(Guid goal, DateOnly day, int ordinal, DateTimeOffset now)
    {
        var id = Id("attempt", goal.ToString("N"), ordinal.ToString(CultureInfo.InvariantCulture));
        return new(goal, id, Id("delivery", id.ToString("N")), Id("operation", id.ToString("N")), day, ordinal, now);
    }
    private static Guid Id(string kind, params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("lilith.daily-note.ledger.v1\0" + kind + "\0" + string.Join('\0', parts)));
        return Guid.ParseExact(Convert.ToHexString(bytes.AsSpan(0, 16)), "N");
    }
    private static bool Token(string value, int length)
        => !string.IsNullOrEmpty(value) && value.Length <= length && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    private static bool Digest(string value)
        => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
