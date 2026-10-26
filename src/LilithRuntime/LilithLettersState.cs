using System.Collections.Immutable;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed record LilithLetterIndexedTurn(LilithLetterTurnReference Source,
    LilithLetterConversationKind Kind, bool Used, bool Withdrawn);
internal sealed record LilithLetterReservation(Guid BatchId, LilithLetterDelivery Receipt, ImmutableArray<Guid> MaterialKeys);
internal sealed record LilithLettersSnapshot(int SchemaVersion, long Revision, string ProfileId,
    string TimeZoneId, string TimeZoneRules, DateTimeOffset HighWaterUtc, LilithLettersSettings Settings,
    LilithLetterBatch? Batch, ImmutableArray<LilithLetterIndexedTurn> Turns,
    ImmutableArray<LilithLetterReservation> Deliveries, LilithLetterEvaluationBudget? Evaluation = null,
    DateTimeOffset? LastPreparationUtc = null);
internal sealed record LilithLetterEvaluationBudget(DateTimeOffset LastStartedUtc, string LocalDay, int Attempts);

// All transitions remain metadata proposals. Only Store.Apply publishes them;
// a future typed executor must independently authorize and prove any PNG effect.
internal static class LilithLettersState
{
    internal static string? EvaluationWaitReason(LilithLettersSnapshot state, DateTimeOffset now, TimeZoneInfo zone, LilithLettersTiming? timing = null)
    {
        CheckTime(state, now);
        if (!state.Settings.Enabled) return "disabled";
        timing ??= LilithLettersTiming.Production;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var prior = state.Evaluation;
        if (prior?.LocalDay == day && prior.Attempts >= timing.EvaluationLimit(state.Settings.DailyLimit, day, now)) return "evaluation_limit_reached";
        return prior is not null && now - prior.LastStartedUtc < timing.Retry(now) ? "evaluation_cooldown" : null;
    }

    internal static LilithLettersSnapshot BeginPreparation(LilithLettersSnapshot state, DateTimeOffset now, LilithLettersTiming? timing = null)
    {
        CheckTime(state, now);
        if (!state.Settings.Enabled || state.LastPreparationUtc is { } prior
            && now - prior < (timing ?? LilithLettersTiming.Production).PreparationRetry(now))
            throw new InvalidOperationException("Letters preparation is cooling down.");
        // This is a separate persisted throttle, not a paid/model evaluation.
        // Restarts and off/on keep it, but a failure cannot exhaust the day.
        return Next(state with { LastPreparationUtc = now }, now);
    }

    internal static LilithLettersSnapshot BeginEvaluation(LilithLettersSnapshot state, DateTimeOffset now, TimeZoneInfo zone, LilithLettersTiming? timing = null)
    {
        if (EvaluationWaitReason(state, now, zone, timing) is not null)
            throw new InvalidOperationException("Letters evaluation budget exhausted.");
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var prior = state.Evaluation;
        var count = prior?.LocalDay == day ? prior.Attempts : 0;
        return Next(state with { Evaluation = new(now, day, count + 1) }, now);
    }
    internal static LilithLettersSnapshot Prune(LilithLettersSnapshot state, DateTimeOffset now)
    {
        CheckTime(state, now);
        // Unknown/reserved receipts never expire. Terminal receipts remain at
        // least 48 hours (including actual delivery day); the monotonic water
        // mark prevents a clock rollback from restoring spent daily quota.
        var deliveries = state.Deliveries.Where(d => d.Receipt.Phase is LilithLetterDeliveryPhase.Reserved or LilithLetterDeliveryPhase.Unknown
            || now - (d.Receipt.DeliveredAtUtc ?? d.Receipt.ReservedAtUtc) < TimeSpan.FromHours(48)).ToImmutableArray();
        var pinned = deliveries.SelectMany(d => d.MaterialKeys).ToHashSet();
        var batch = state.Batch;
        if (batch is not null && now - batch.LastChatUtc >= LilithLettersPolicy.MaximumContextAge) batch = null;
        var turns = state.Turns.Where(t => pinned.Contains(t.Source.Key)
            || !t.Used && (now - t.Source.CompletedAtUtc < LilithLettersPolicy.MaximumContextAge
                || batch is not null && t.Source.CompletedAtUtc >= batch.FirstChatUtc && t.Source.CompletedAtUtc <= batch.LastChatUtc)).ToImmutableArray();
        if (turns.Length == state.Turns.Length && deliveries.Length == state.Deliveries.Length && batch == state.Batch) return state;
        return Next(state with { Batch = batch, Turns = turns, Deliveries = deliveries }, now);
    }

    internal static LilithLettersSnapshot Create(string profile, TimeZoneInfo zone, LilithLettersSettings settings)
    {
        LilithLettersHistory.Identity(profile);
        return new(1, 0, profile, zone.Id, LilithLettersHistory.Hash(zone.ToSerializedString()),
            DateTimeOffset.UnixEpoch, settings, null, [], []);
    }

    internal static LilithLettersSnapshot Record(LilithLettersSnapshot state, LilithLetterTurnReference source,
        DateTimeOffset now)
    {
        CheckTime(state, now); LilithLettersHistory.Validate(source);
        if (source.ProfileId != state.ProfileId || source.CompletedAtUtc > now)
            throw new InvalidDataException("Letter source binding or time mismatch.");
        if (!state.Settings.Enabled || now - source.CompletedAtUtc >= LilithLettersPolicy.MaximumContextAge) return state;
        var previous = state.Turns.FirstOrDefault(t => t.Source.Key == source.Key);
        if (previous is not null)
        {
            if (previous.Source != source) throw new InvalidDataException("Letter source identity changed.");
            return state;
        }
        if (state.Turns.Length >= LilithLettersHistory.MaximumTurns)
            throw new InvalidDataException("Letter index capacity reached; no automatic reset.");
        return Next(state with { Turns = state.Turns.Add(new(source, LilithLetterConversationKind.Uncertain, false, false)) }, now);
    }

    internal static LilithLettersSnapshot Classify(LilithLettersSnapshot state, Guid key,
        LilithLetterConversationKind kind, DateTimeOffset now)
    {
        CheckTime(state, now);
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid letter classification.");
        if (!state.Settings.Enabled) return state;
        var index = Find(state, key); var item = state.Turns[index];
        if (item.Used || item.Withdrawn || item.Kind != LilithLetterConversationKind.Uncertain
            || now - item.Source.CompletedAtUtc >= LilithLettersPolicy.MaximumContextAge) return state;
        // Classification is a proposal, not proof of source validity or permission.
        return Next(state with { Turns = state.Turns.SetItem(index, item with { Kind = kind }) }, now);
    }

    internal static LilithLettersSnapshot Collect(LilithLettersSnapshot state, DateTimeOffset now, LilithLettersTiming? timing = null)
    {
        CheckTime(state, now);
        if (!state.Settings.Enabled) return state;
        var batch = state.Batch;
        // An expired unsent batch must not monopolize the next day's material.
        // Keep its indexed tombstones; the monotonic clock excludes old replay.
        if (batch is not null && now - batch.LastChatUtc >= LilithLettersPolicy.MaximumContextAge) batch = null;
        foreach (var item in state.Turns.Where(t => !t.Used && !t.Withdrawn
                     && t.Kind == LilithLetterConversationKind.Conversation
                     && now - t.Source.CompletedAtUtc < LilithLettersPolicy.MaximumContextAge)
                 .OrderBy(t => t.Source.CompletedAtUtc).ThenBy(t => t.Source.Key))
            batch = LilithLettersPolicy.Accumulate(batch,
                new(item.Source.Key, item.Source.CompletedAtUtc, item.Kind, true), now, timing);
        if (batch is not null) batch = LilithLettersPolicy.Seal(batch, now, timing);
        return batch == state.Batch ? state : Next(state with { Batch = batch }, now);
    }

    internal static LilithLettersSnapshot Resume(LilithLettersSnapshot state, DateTimeOffset now)
    {
        CheckTime(state, now);
        var batch = state.Batch is null ? null : LilithLettersPolicy.ResumeSealed(state.Batch);
        return Next(state with { Batch = batch }, now);
    }

    internal static LilithLettersSnapshot Configure(LilithLettersSnapshot state, LilithLettersSettings settings,
        DateTimeOffset now) { CheckTime(state, now); return Next(state with { Settings = settings }, now); }

    internal static LilithLettersSnapshot Withdraw(LilithLettersSnapshot state, Guid key, DateTimeOffset now)
    {
        CheckTime(state, now); var index = Find(state, key);
        // Keep a tombstone, including while disabled, so old text cannot return.
        // Existing unknown/delivered receipts survive this cancellation.
        return Next(state with { Turns = state.Turns.SetItem(index, state.Turns[index] with { Withdrawn = true }), Batch = null }, now);
    }

    internal static LilithLettersSnapshot ReconcileAttestedSources(LilithLettersSnapshot state,
        ImmutableArray<Guid> unattestedKeys, DateTimeOffset now, LilithLettersTiming? timing = null)
    {
        CheckTime(state, now);
        if (unattestedKeys.IsDefault || unattestedKeys.Length > LilithLettersHistory.MaximumTurns
            || unattestedKeys.Any(key => key == Guid.Empty) || unattestedKeys.Distinct().Count() != unattestedKeys.Length)
            throw new InvalidDataException("Letter source reconciliation is not bounded.");
        // Only the host's currently verified scope may supply these keys. This
        // retires eligibility in this short-lived LETTER index, not the source
        // chat or memory. Reserved/delivered evidence is never released here.
        var items = unattestedKeys.Select(key => state.Turns[Find(state, key)]).ToArray();
        if (items.Any(item => item.Used)) throw new InvalidDataException("Reserved letter sources cannot be reconciled away.");
        var changed = items.Where(item => !item.Withdrawn).ToArray();
        if (!state.Settings.Enabled || changed.Length == 0) return state;
        var keys = changed.Select(item => item.Source.Key).ToHashSet();
        var batch = state.Batch;
        if (batch is not null && changed.Any(item => item.Kind == LilithLetterConversationKind.Conversation
                && item.Source.CompletedAtUtc >= batch.FirstChatUtc && item.Source.CompletedAtUtc <= batch.LastChatUtc))
            batch = null;
        // One Store publication covers tombstones and rebuilding the affected
        // batch. Unrelated exclusions leave a valid sealed/startup batch intact.
        var reconciled = Next(state with { Turns = state.Turns.Select(item => keys.Contains(item.Source.Key)
            ? item with { Withdrawn = true } : item).ToImmutableArray(), Batch = batch }, now);
        return Collect(reconciled, now, timing);
    }

    internal static LilithLettersSnapshot Reserve(LilithLettersSnapshot state, Guid deliveryId,
        LilithLetterOpportunity opportunity, bool materialSufficient, TimeZoneInfo zone, LilithLettersTiming? timing = null)
    {
        CheckTime(state, opportunity.NowUtc);
        if (zone.Id != state.TimeZoneId || LilithLettersHistory.Hash(zone.ToSerializedString()) != state.TimeZoneRules
            || deliveryId == Guid.Empty || state.Deliveries.Any(d => d.Receipt.DeliveryId == deliveryId))
            throw new InvalidDataException("Letter reservation binding mismatch.");
        var gate = LilithLettersPolicy.Decide(state.Settings, state.Batch, opportunity, materialSufficient,
            new(state.Deliveries.Select(d => d.Receipt)), zone, timing);
        if (gate != LilithLetterGate.Ready) throw new InvalidOperationException("Letter reservation is not ready: " + gate);
        var batch = state.Batch!;
        var selected = state.Turns.Where(t => !t.Used && !t.Withdrawn && t.Kind == LilithLetterConversationKind.Conversation
            && t.Source.CompletedAtUtc >= batch.FirstChatUtc && t.Source.CompletedAtUtc <= batch.LastChatUtc).ToArray();
        if (!selected.Any(t => t.Source.Key == batch.FirstTurnId) || !selected.Any(t => t.Source.Key == batch.LastTurnId)
            || state.Deliveries.Length >= 4096) throw new InvalidDataException("Letter reservation material or capacity invalid.");
        var keys = selected.Select(t => t.Source.Key).ToHashSet();
        return Next(state with
        {
            Batch = null,
            Turns = state.Turns.Select(t => keys.Contains(t.Source.Key) ? t with { Used = true } : t).ToImmutableArray(),
            Deliveries = state.Deliveries.Add(new(batch.BatchId, new(deliveryId, opportunity.NowUtc, LilithLetterDeliveryPhase.Reserved),
                keys.OrderBy(k => k).ToImmutableArray()))
        }, opportunity.NowUtc);
    }

    internal static LilithLettersSnapshot Resolve(LilithLettersSnapshot state, Guid deliveryId,
        LilithLetterDeliveryPhase phase, DateTimeOffset now, DateTimeOffset? deliveredAtUtc = null)
    {
        CheckTime(state, now);
        if (phase is not (LilithLetterDeliveryPhase.Unknown or LilithLetterDeliveryPhase.Delivered or LilithLetterDeliveryPhase.ConfirmedNoWrite))
            throw new ArgumentException("Invalid letter resolution.");
        var index = -1;
        for (var i = 0; i < state.Deliveries.Length; i++) if (state.Deliveries[i].Receipt.DeliveryId == deliveryId) index = i;
        if (index < 0) throw new InvalidDataException("Unknown letter delivery identity.");
        var entry = state.Deliveries[index];
        if ((phase == LilithLetterDeliveryPhase.Delivered) != deliveredAtUtc.HasValue
            || deliveredAtUtc < entry.Receipt.ReservedAtUtc || deliveredAtUtc > now)
            throw new InvalidDataException("Letter receipt timestamp is not bound to its delivery.");
        if (deliveredAtUtc is { } stamp) LilithLettersPolicy.RequireUtc(stamp);
        if (entry.Receipt.Phase is LilithLetterDeliveryPhase.Delivered or LilithLetterDeliveryPhase.ConfirmedNoWrite)
        {
            if (entry.Receipt.Phase != phase || entry.Receipt.DeliveredAtUtc != deliveredAtUtc)
                throw new InvalidDataException("Terminal receipt cannot regress.");
            return state;
        }
        // Only a future verified executor/reconciler may supply this result.
        // No RPC or model payload is accepted here and no effect is performed.
        var receipt = entry.Receipt with { Phase = phase, DeliveredAtUtc = deliveredAtUtc };
        return Next(state with { Deliveries = state.Deliveries.SetItem(index, entry with { Receipt = receipt }) }, now);
    }

    internal static void Validate(LilithLettersSnapshot state)
    {
        LilithLettersHistory.Identity(state.ProfileId); LilithLettersHistory.Identity(state.TimeZoneId);
        LilithLettersPolicy.RequireUtc(state.HighWaterUtc);
        if (state.LastPreparationUtc is { } preparation)
        {
            LilithLettersPolicy.RequireUtc(preparation);
            if (preparation > state.HighWaterUtc) throw new InvalidDataException("Invalid letter preparation throttle.");
        }
        if (state.Evaluation is { } budget)
        {
            LilithLettersPolicy.RequireUtc(budget.LastStartedUtc);
            if (budget.LastStartedUtc > state.HighWaterUtc || budget.Attempts is < 1 or > 30
                || !DateOnly.TryParseExact(budget.LocalDay, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _)) throw new InvalidDataException("Invalid letter evaluation budget.");
        }
        if (state.SchemaVersion != 1 || state.Revision < 0 || state.Settings is null
            || state.Turns.IsDefault || state.Turns.Length > LilithLettersHistory.MaximumTurns
            || state.Deliveries.IsDefault || state.Deliveries.Length > 4096
            || state.Turns.Select(t => t.Source.Key).Distinct().Count() != state.Turns.Length
            || state.Deliveries.Any(d => d.BatchId == Guid.Empty)) throw new InvalidDataException("Invalid letter state.");
        _ = new LilithLetterQuota(state.Deliveries.Select(d => d.Receipt));
        var reservedKeys = new HashSet<Guid>();
        foreach (var delivery in state.Deliveries)
        {
            if (delivery.MaterialKeys.IsDefaultOrEmpty || delivery.MaterialKeys.Length > LilithLettersHistory.MaximumTurns)
                throw new InvalidDataException("Letter reservation has no bounded material binding.");
            foreach (var key in delivery.MaterialKeys)
                if (!reservedKeys.Add(key) || !state.Turns.Any(t => t.Source.Key == key && t.Used
                        && t.Source.CompletedAtUtc <= delivery.Receipt.ReservedAtUtc))
                    throw new InvalidDataException("Letter reserved material cannot be reused or detached.");
        }
        foreach (var item in state.Turns)
        {
            LilithLettersHistory.Validate(item.Source);
            if (!Enum.IsDefined(item.Kind) || item.Source.ProfileId != state.ProfileId || item.Source.CompletedAtUtc > state.HighWaterUtc
                || item.Used != reservedKeys.Contains(item.Source.Key))
                throw new InvalidDataException("Invalid indexed letter source.");
        }
        if (state.Deliveries.Any(d => d.Receipt.ReservedAtUtc > state.HighWaterUtc || d.Receipt.DeliveredAtUtc > state.HighWaterUtc))
            throw new InvalidDataException("Letter receipt exceeds clock high-water.");
        if (state.Batch is { } batch)
        {
            LilithLettersPolicy.Validate(batch);
            if (batch.LastChatUtc > state.HighWaterUtc || batch.SealedAtUtc > state.HighWaterUtc
                || !state.Turns.Any(t => t.Source.Key == batch.FirstTurnId && !t.Used && !t.Withdrawn
                    && t.Kind == LilithLetterConversationKind.Conversation && t.Source.CompletedAtUtc == batch.FirstChatUtc)
                || !state.Turns.Any(t => t.Source.Key == batch.LastTurnId && !t.Used && !t.Withdrawn
                    && t.Kind == LilithLetterConversationKind.Conversation && t.Source.CompletedAtUtc == batch.LastChatUtc))
                throw new InvalidDataException("Letter batch has no current source.");
        }
    }
    private static void CheckTime(LilithLettersSnapshot state, DateTimeOffset now)
    { Validate(state); LilithLettersPolicy.RequireUtc(now); if (now < state.HighWaterUtc) throw new InvalidDataException("Letter clock regressed."); }
    private static LilithLettersSnapshot Next(LilithLettersSnapshot state, DateTimeOffset now)
    { var next = state with { Revision = checked(state.Revision + 1), HighWaterUtc = now }; Validate(next); return next; }
    private static int Find(LilithLettersSnapshot state, Guid key)
    { for (var i = 0; i < state.Turns.Length; i++) if (state.Turns[i].Source.Key == key) return i; throw new InvalidDataException("Unknown letter source."); }
}
