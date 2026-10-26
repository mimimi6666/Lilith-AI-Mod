using LilithRuntimeContracts;

namespace LilithRuntime;

internal enum LilithLetterConversationKind { Uncertain, ToolOnly, Conversation }
internal enum LilithLetterGate
{
    Disabled, ClockUntrusted, NoConversation, MaterialInsufficient, Accumulating,
    ContextExpired, AwaitingAbsence, Busy, GameUnavailable, NativeUnavailable,
    DailyLimitReached, UnresolvedDelivery, Ready
}

// Metadata only. The future trusted history/semantic adapter owns this evidence;
// model output alone cannot construct an authorized game operation.
internal sealed record LilithLetterConversationEvidence(Guid TurnId, DateTimeOffset AtUtc,
    LilithLetterConversationKind Kind, bool HasFreshMaterial);

internal sealed record LilithLetterBatch(Guid BatchId, Guid FirstTurnId, Guid LastTurnId,
    DateTimeOffset FirstChatUtc, DateTimeOffset LastChatUtc, DateTimeOffset? SealedAtUtc = null,
    bool PendingAtRestart = false);

internal sealed record LilithLetterOpportunity(DateTimeOffset NowUtc, bool ClockTrusted,
    bool PlayerAbsent, TimeSpan ContinuousAbsence, bool Busy, bool GameReady, bool NativeReady);

internal sealed record LilithLetterContextWindow(DateTimeOffset MainFromUtc, DateTimeOffset MainToUtc,
    DateTimeOffset BackdropFromUtc, DateTimeOffset PreferredBackdropFromUtc);

// A disconnected, side-effect-free proposal. Ready must still be rechecked and
// durably reserved by a future host before the existing typed delivery chain.
internal static class LilithLettersPolicy
{
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromHours(4);
    internal static readonly TimeSpan MaximumCollection = TimeSpan.FromHours(8);
    internal static readonly TimeSpan MaximumContextAge = TimeSpan.FromHours(24);
    internal static readonly TimeSpan MinimumAbsence = TimeSpan.FromMinutes(15);

    internal static LilithLetterBatch? Accumulate(LilithLetterBatch? batch,
        LilithLetterConversationEvidence evidence, DateTimeOffset observedAtUtc, LilithLettersTiming? timing = null)
    {
        RequireUtc(observedAtUtc); RequireUtc(evidence.AtUtc);
        if (evidence.TurnId == Guid.Empty || evidence.AtUtc > observedAtUtc || !Enum.IsDefined(evidence.Kind))
            throw new ArgumentException("Invalid letter conversation metadata.");
        if (batch is not null) Validate(batch);
        if (evidence.Kind != LilithLetterConversationKind.Conversation || !evidence.HasFreshMaterial)
            return batch;
        if (batch is null)
            return new(Guid.NewGuid(), evidence.TurnId, evidence.TurnId, evidence.AtUtc, evidence.AtUtc);
        if (batch.SealedAtUtc is not null || evidence.AtUtc < batch.LastChatUtc
            || evidence.AtUtc >= batch.FirstChatUtc + (timing ?? LilithLettersTiming.Production).Collection(observedAtUtc)
            || evidence.TurnId == batch.FirstTurnId || evidence.TurnId == batch.LastTurnId)
            return batch;
        return batch with { LastTurnId = evidence.TurnId, LastChatUtc = evidence.AtUtc };
    }

    internal static LilithLetterBatch Seal(LilithLetterBatch batch, DateTimeOffset nowUtc, LilithLettersTiming? timing = null)
    {
        Validate(batch); RequireUtc(nowUtc);
        if (nowUtc < batch.FirstChatUtc) throw new ArgumentException("Clock precedes letter batch.");
        if (batch.SealedAtUtc is not null) return batch;
        var duration = (timing ?? LilithLettersTiming.Production).Collection(nowUtc);
        // An existing production batch must never seal before its last real turn.
        if (batch.LastChatUtc >= batch.FirstChatUtc + duration) duration = MaximumCollection;
        return nowUtc >= batch.FirstChatUtc + duration
            ? batch with { SealedAtUtc = batch.FirstChatUtc + duration } : batch;
    }

    internal static LilithLetterBatch ResumeSealed(LilithLetterBatch persisted)
    {
        Validate(persisted);
        // Persisted sealing, not merely time passing while the app was closed,
        // is the proof for the user-requested startup priority exception.
        return persisted.SealedAtUtc is not null ? persisted with { PendingAtRestart = true } : persisted;
    }

    internal static LilithLetterContextWindow ContextWindow(LilithLetterBatch batch, DateTimeOffset nowUtc)
    {
        Validate(batch); RequireUtc(nowUtc);
        if (nowUtc < batch.FirstChatUtc) throw new ArgumentException("Clock precedes letter batch.");
        var end = nowUtc < batch.FirstChatUtc + MaximumCollection ? nowUtc : batch.FirstChatUtc + MaximumCollection;
        // Backdrop is relative to this captured batch, never a scan of all history.
        return new(batch.FirstChatUtc, end, end - MaximumContextAge, end - TimeSpan.FromHours(12));
    }

    internal static LilithLetterGate Decide(LilithLettersSettings settings, LilithLetterBatch? batch,
        LilithLetterOpportunity opportunity, bool materialSufficient, LilithLetterQuota quota, TimeZoneInfo zone, LilithLettersTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(zone); RequireUtc(opportunity.NowUtc);
        if (opportunity.ContinuousAbsence < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(opportunity));
        if (!settings.Enabled) return LilithLetterGate.Disabled;
        if (!opportunity.ClockTrusted) return LilithLetterGate.ClockUntrusted;
        if (batch is null) return LilithLetterGate.NoConversation;
        Validate(batch);
        if (opportunity.NowUtc < batch.LastChatUtc) return LilithLetterGate.ClockUntrusted;
        if (quota.HasUnresolved) return LilithLetterGate.UnresolvedDelivery;
        if (quota.CountForDay(opportunity.NowUtc, zone) >= settings.DailyLimit) return LilithLetterGate.DailyLimitReached;
        if (!materialSufficient) return LilithLetterGate.MaterialInsufficient;
        if (opportunity.NowUtc - batch.LastChatUtc >= MaximumContextAge) return LilithLetterGate.ContextExpired;
        if (opportunity.NowUtc - batch.FirstChatUtc < (timing ?? LilithLettersTiming.Production).Age(opportunity.NowUtc)) return LilithLetterGate.Accumulating;
        if (!opportunity.GameReady) return LilithLetterGate.GameUnavailable;
        if (!opportunity.NativeReady) return LilithLetterGate.NativeUnavailable;
        if (opportunity.Busy) return LilithLetterGate.Busy;
        if (timing?.AllowsPresentTest(opportunity.NowUtc) != true
            && (!batch.PendingAtRestart || timing?.IsTest(opportunity.NowUtc) == true)
            && (!opportunity.PlayerAbsent || opportunity.ContinuousAbsence < (timing ?? LilithLettersTiming.Production).Absence(opportunity.NowUtc)))
            return LilithLetterGate.AwaitingAbsence;
        return LilithLetterGate.Ready;
    }

    internal static void Validate(LilithLetterBatch batch)
    {
        RequireUtc(batch.FirstChatUtc); RequireUtc(batch.LastChatUtc);
        if (batch.BatchId == Guid.Empty || batch.FirstTurnId == Guid.Empty || batch.LastTurnId == Guid.Empty
            || batch.LastChatUtc < batch.FirstChatUtc || batch.LastChatUtc >= batch.FirstChatUtc + MaximumCollection
            || batch.SealedAtUtc is { } seal && (seal <= batch.LastChatUtc ||
                (seal != batch.FirstChatUtc + MaximumCollection && seal != batch.FirstChatUtc + TimeSpan.FromMinutes(10)))
            || batch.PendingAtRestart && batch.SealedAtUtc is null)
            throw new ArgumentException("Invalid letter batch metadata.");
    }

    internal static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value < DateTimeOffset.UnixEpoch || value > DateTimeOffset.MaxValue - TimeSpan.FromDays(2))
            throw new ArgumentException("Letter timestamp must be bounded UTC.");
    }
}

internal enum LilithLetterDeliveryPhase { Reserved, Unknown, Delivered, ConfirmedNoWrite }
internal sealed record LilithLetterDelivery(Guid DeliveryId, DateTimeOffset ReservedAtUtc,
    LilithLetterDeliveryPhase Phase, DateTimeOffset? DeliveredAtUtc = null);

// Pure receipt accounting, NOT a durable ledger or permission authority. A
// future store must commit reservations and prohibit removal/backward transitions.
internal sealed class LilithLetterQuota
{
    private readonly LilithLetterDelivery[] _entries;
    internal LilithLetterQuota(IEnumerable<LilithLetterDelivery> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.Take(4097).ToArray();
        if (_entries.Length > 4096) throw new ArgumentException("Letter quota metadata capacity exceeded.");
        var ids = new HashSet<Guid>();
        foreach (var entry in _entries)
        {
            if (entry is null) throw new ArgumentException("Missing letter receipt metadata.");
            LilithLettersPolicy.RequireUtc(entry.ReservedAtUtc);
            if (entry.DeliveryId == Guid.Empty || !ids.Add(entry.DeliveryId) || !Enum.IsDefined(entry.Phase)
                || (entry.Phase == LilithLetterDeliveryPhase.Delivered) != entry.DeliveredAtUtc.HasValue)
                throw new ArgumentException("Invalid letter receipt metadata.");
            if (entry.DeliveredAtUtc is { } delivered)
            {
                LilithLettersPolicy.RequireUtc(delivered);
                if (delivered < entry.ReservedAtUtc) throw new ArgumentException("Receipt predates reservation.");
            }
        }
    }
    internal bool HasUnresolved => _entries.Any(x => x.Phase is LilithLetterDeliveryPhase.Reserved or LilithLetterDeliveryPhase.Unknown);
    internal int CountForDay(DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        LilithLettersPolicy.RequireUtc(nowUtc); ArgumentNullException.ThrowIfNull(zone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
        // A future receipt signals a clock rollback; callers must not gain quota.
        if (_entries.Any(x => x.ReservedAtUtc > nowUtc || x.DeliveredAtUtc > nowUtc)) return int.MaxValue;
        return _entries.Count(x => x.Phase == LilithLetterDeliveryPhase.Delivered
            && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.DeliveredAtUtc!.Value, zone).DateTime) == day);
    }
}
