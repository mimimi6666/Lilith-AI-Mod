using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LilithRuntimeContracts;
using NativeDateTime = Il2CppSystem.DateTime;
using NativeNullableDateTime = Il2CppSystem.Nullable<Il2CppSystem.DateTime>;

namespace LilithTextInjector;

/// <summary>
/// Main-thread-only adapter over the game's official MemoBook API. Native
/// MemoEntry instances never cross this boundary; every result is projected to
/// immutable runtime-contract DTOs before returning.
/// </summary>
internal static class LilithNativeMemoBookAdapter
{
    internal static LilithMemoResult ExecuteOnMainThread(
        LilithMemoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            if (!OfficialArchiveIsReady())
                return Failure(command, "official_notes_unavailable");

            return command.Action switch
            {
                LilithMemoAction.List => List(command),
                LilithMemoAction.Get => Get(command),
                LilithMemoAction.CreateScratch => CreateScratch(command),
                LilithMemoAction.CreateTodo => CreateTodo(command),
                LilithMemoAction.Update => Update(command),
                LilithMemoAction.Delete => Delete(command),
                LilithMemoAction.Promote => Promote(command),
                LilithMemoAction.Complete => Complete(command),
                _ => Failure(command, "unsupported_action")
            };
        }
        catch (Exception exception)
        {
            Plugin.PluginLog?.LogWarning(
                "Official MemoBook operation reached a terminal fault ("
                + exception.GetType().Name
                + ").");
            return RecoverTerminalFailure(command);
        }
    }

    private static bool OfficialArchiveIsReady()
    {
        var archive = global::Archive.Instance;
        return archive is not null && archive.memos is not null;
    }

    private static LilithMemoResult List(LilithMemoCommand command)
    {
        var items = new List<LilithMemoItem>(command.Limit);
        var seen = new HashSet<int>();
        var keyword = command.Query ?? string.Empty;

        if (command.Kind is LilithMemoKind.All or LilithMemoKind.Scratch
            && command.Filter != LilithMemoFilter.Done)
        {
            Append(
                global::MemoBook.QueryScratch(keyword, includeArchived: false),
                command.Limit,
                items,
                seen);
        }

        if (items.Count < command.Limit
            && command.Kind is LilithMemoKind.All or LilithMemoKind.Todo)
        {
            Append(
                global::MemoBook.Query(
                    new NativeNullableDateTime(),
                    keyword,
                    ToNativeFilter(command.Filter)),
                command.Limit,
                items,
                seen);
        }

        return Success(command, items);
    }

    private static LilithMemoResult Get(LilithMemoCommand command)
    {
        if (!global::MemoBook.TryGet(command.MemoId, out var entry))
            return Failure(command, "not_found");
        return Success(command, ToDto(entry));
    }

    private static LilithMemoResult CreateScratch(LilithMemoCommand command)
    {
        if (command.Text is null || !command.ScratchTier.HasValue)
            return Failure(command, "invalid_command");

        global::MemoEntry? created = null;
        try
        {
            created = global::MemoBook.CreateScratch(
                command.Text,
                ToNativeScratchTier(command.ScratchTier.Value));
            if (created is null
                || !global::MemoBook.TryGet(created.id, out var current))
            {
                return created is null
                    ? Failure(command, "create_postcondition_failed")
                    : RollBackFailedCreate(
                        command,
                        created.id,
                        "create_postcondition_failed");
            }
            if (!global::MemoBook.IsScratch(current)
                || current.scratch != (int)command.ScratchTier.Value
                || !string.Equals(
                    current.text,
                    command.Text,
                    StringComparison.Ordinal))
            {
                return RollBackFailedCreate(
                    command,
                    current.id,
                    "create_postcondition_failed");
            }
            return Success(command, ToDto(current));
        }
        catch
        {
            if (created is not null)
            {
                return RollBackFailedCreate(
                    command,
                    created.id,
                    "official_operation_fault");
            }
            throw;
        }
    }

    private static LilithMemoResult CreateTodo(LilithMemoCommand command)
    {
        if (command.Text is null)
            return Failure(command, "invalid_command");
        if (LilithMemoMutationPolicy.HasReminderCountdownConflict(
                currentRemind: false,
                currentCountdown: false,
                command.Remind,
                command.Countdown))
        {
            return Failure(command, "reminder_countdown_conflict");
        }

        global::MemoEntry? created = null;
        try
        {
            var defaultDay = ParseOptionalNativeDate(command.Date);
            created = global::MemoBook.CreateTodoFromText(
                command.Text,
                defaultDay);
            if (created is null
                || !global::MemoBook.TryGet(created.id, out var current))
            {
                return created is null
                    ? Failure(command, "create_postcondition_failed")
                    : RollBackFailedCreate(
                        command,
                        created.id,
                        "create_postcondition_failed");
            }

            if (LilithMemoMutationPolicy.HasReminderCountdownConflict(
                    current.remind,
                    current.countdown,
                    command.Remind,
                    command.Countdown))
            {
                return RollBackFailedCreate(
                    command,
                    current.id,
                    "reminder_countdown_conflict");
            }

            ApplyTodoScheduling(current, command);
            if (!global::MemoBook.TryGet(current.id, out var fresh))
            {
                return RollBackFailedCreate(
                    command,
                    current.id,
                    "create_postcondition_failed");
            }
            if (global::MemoBook.IsScratch(fresh)
                || !TodoSchedulingMatches(fresh, command))
            {
                return RollBackFailedCreate(
                    command,
                    fresh.id,
                    "create_postcondition_failed");
            }
            return Success(command, ToDto(fresh));
        }
        catch
        {
            if (created is not null)
            {
                return RollBackFailedCreate(
                    command,
                    created.id,
                    "official_operation_fault");
            }
            throw;
        }
    }

    private static LilithMemoResult Update(LilithMemoCommand command)
    {
        if (!TryGetForMutation(command, out var entry, out var failure))
            return failure!;

        var isScratch = global::MemoBook.IsScratch(entry);
        if (isScratch && HasTodoOnlyUpdate(command))
            return Failure(command, "scratch_field_mismatch", ToDto(entry));
        if (!isScratch && command.ScratchTier.HasValue)
            return Failure(command, "todo_field_mismatch", ToDto(entry));
        if (!isScratch
            && LilithMemoMutationPolicy.HasReminderCountdownConflict(
                entry.remind,
                entry.countdown,
                command.Remind,
                command.Countdown))
        {
            return Failure(
                command,
                "reminder_countdown_conflict",
                ToDto(entry));
        }

        System.DateTime? managedDate = command.Date is null
            ? null
            : ParseManagedDate(command.Date);

        if (command.Text is not null
            && !string.Equals(entry.text, command.Text, StringComparison.Ordinal))
        {
            entry.text = command.Text;
            global::MemoBook.Touch(entry);
        }
        if (managedDate.HasValue)
            global::MemoBook.SetDate(entry, ToNativeDate(managedDate.Value));
        if (isScratch && command.ScratchTier.HasValue)
        {
            global::MemoBook.SetScratchTier(
                entry,
                ToNativeScratchTier(command.ScratchTier.Value));
        }
        if (!isScratch)
            ApplyTodoScheduling(entry, command);

        if (!global::MemoBook.TryGet(entry.id, out var fresh))
            return Failure(command, "mutation_postcondition_failed");
        if (!UpdateMatches(fresh, command, isScratch))
        {
            return Failure(
                command,
                "mutation_postcondition_failed",
                SafeDto(fresh));
        }
        return Success(command, ToDto(fresh));
    }

    private static LilithMemoResult Delete(LilithMemoCommand command)
    {
        if (!TryGetForMutation(command, out var entry, out var failure))
            return failure!;

        _ = global::MemoBook.Remove(entry.id);
        if (!global::MemoBook.TryGet(entry.id, out var remaining))
            return Success(command);
        return Failure(
            command,
            "mutation_postcondition_failed",
            SafeDto(remaining));
    }

    private static LilithMemoResult Promote(LilithMemoCommand command)
    {
        if (!TryGetForMutation(command, out var entry, out var failure))
            return failure!;
        if (!global::MemoBook.IsScratch(entry))
            return Failure(command, "not_scratch", ToDto(entry));

        var day = ParseOptionalNativeDate(command.Date);
        global::MemoBook.PromoteToTodo(entry, day);
        if (!global::MemoBook.TryGet(entry.id, out var fresh))
            return Failure(command, "mutation_postcondition_failed");
        if (global::MemoBook.IsScratch(fresh)
            || command.Date is not null
                && !string.Equals(
                    fresh.date,
                    command.Date,
                    StringComparison.Ordinal))
        {
            return Failure(
                command,
                "mutation_postcondition_failed",
                SafeDto(fresh));
        }
        return Success(command, ToDto(fresh));
    }

    private static LilithMemoResult Complete(LilithMemoCommand command)
    {
        if (!TryGetForMutation(command, out var entry, out var failure))
            return failure!;
        if (global::MemoBook.IsScratch(entry))
            return Failure(command, "not_todo", ToDto(entry));
        if (!command.Done.HasValue)
            return Failure(command, "invalid_command", ToDto(entry));

        var occurrence = command.Date is null
            ? System.DateTime.Today
            : ParseManagedDate(command.Date);
        var nativeOccurrence = ToNativeDate(occurrence);

        // The occurrence is always explicit. This is required for recurring
        // todos, where completion belongs to one calendar occurrence rather
        // than mutating a detached duplicate task.
        global::MemoBook.SetDone(
            entry,
            command.Done.Value,
            new NativeNullableDateTime(nativeOccurrence));

        if (!global::MemoBook.TryGet(entry.id, out var fresh))
            return Failure(command, "mutation_postcondition_failed");

        var completedAsRequested = fresh.repeat == (int)global::MemoRepeat.None
            ? fresh.done == command.Done.Value
            : global::MemoBook.IsDoneOn(fresh, nativeOccurrence)
                == command.Done.Value;
        if (!completedAsRequested)
        {
            return Failure(
                command,
                "mutation_postcondition_failed",
                SafeDto(fresh));
        }
        return Success(command, ToDto(fresh));
    }

    private static bool TryGetForMutation(
        LilithMemoCommand command,
        out global::MemoEntry entry,
        out LilithMemoResult? failure)
    {
        if (!global::MemoBook.TryGet(command.MemoId, out entry))
        {
            failure = Failure(command, "not_found");
            return false;
        }
        if (!LilithMemoMutationPolicy.ExactUpdatedTimeMatches(
                entry.updatedTime,
                command.ExpectedUpdatedTime))
        {
            failure = Failure(command, "version_conflict", ToDto(entry));
            return false;
        }
        failure = null;
        return true;
    }

    private static void ApplyTodoScheduling(
        global::MemoEntry entry,
        LilithMemoCommand command)
    {
        // Disable the conflicting state first so the official API never sees
        // an intermediate reminder+countdown combination.
        if (command.Countdown == false && entry.countdown)
            global::MemoBook.SetCountdown(entry, false);
        if (command.Remind == false && entry.remind)
            global::MemoBook.SetRemind(entry, false);

        if (command.Repeat.HasValue)
        {
            global::MemoBook.SetRepeat(
                entry,
                ToNativeRepeat(command.Repeat.Value));
        }
        if (command.Pinned.HasValue)
            global::MemoBook.SetPinned(entry, command.Pinned.Value);

        if (command.Remind == true)
            global::MemoBook.SetRemind(entry, true);

        if (command.RemindHour.HasValue && command.RemindMinute.HasValue)
        {
            if ((command.Remind ?? entry.remind)
                && command.RemindHour.Value == 0
                && command.RemindMinute.Value == 0)
            {
                // Midnight is a valid reminder time, not the disabled
                // sentinel. Preserve the official order explicitly.
                global::MemoBook.SetRemind(entry, true);
            }
            global::MemoBook.SetRemindTime(
                entry,
                command.RemindHour.Value,
                command.RemindMinute.Value);
        }

        if (command.Countdown == true)
            global::MemoBook.SetCountdown(entry, true);
    }

    private static bool TodoSchedulingMatches(
        global::MemoEntry entry,
        LilithMemoCommand command)
        => !global::MemoBook.IsScratch(entry)
            && (!command.Remind.HasValue
                || entry.remind == command.Remind.Value)
            && (!command.RemindHour.HasValue
                || entry.remindHour == command.RemindHour.Value
                    && entry.remindMinute == command.RemindMinute!.Value)
            && (!command.Repeat.HasValue
                || entry.repeat == (int)command.Repeat.Value)
            && (!command.Pinned.HasValue
                || entry.pinned == command.Pinned.Value)
            && (!command.Countdown.HasValue
                || entry.countdown == command.Countdown.Value)
            && !(entry.remind && entry.countdown);

    private static bool UpdateMatches(
        global::MemoEntry entry,
        LilithMemoCommand command,
        bool expectedScratch)
        => global::MemoBook.IsScratch(entry) == expectedScratch
            && (command.Text is null
                || string.Equals(entry.text, command.Text, StringComparison.Ordinal))
            && (command.Date is null
                || string.Equals(entry.date, command.Date, StringComparison.Ordinal))
            && (!command.ScratchTier.HasValue
                || entry.scratch == (int)command.ScratchTier.Value)
            && TodoSchedulingMatchesOrScratch(entry, command, expectedScratch);

    private static bool TodoSchedulingMatchesOrScratch(
        global::MemoEntry entry,
        LilithMemoCommand command,
        bool expectedScratch)
        => expectedScratch || TodoSchedulingMatches(entry, command);

    private static bool HasTodoOnlyUpdate(LilithMemoCommand command)
        => command.Date is not null
            || command.Remind.HasValue
            || command.RemindHour.HasValue
            || command.Repeat.HasValue
            || command.Pinned.HasValue
            || command.Countdown.HasValue;

    private static LilithMemoResult RollBackFailedCreate(
        LilithMemoCommand command,
        int memoId,
        string failureCode)
    {
        _ = global::MemoBook.Remove(memoId);
        if (!global::MemoBook.TryGet(memoId, out var remaining))
            return Failure(command, failureCode);
        return Failure(
            command,
            "create_rollback_failed",
            SafeDto(remaining));
    }

    private static LilithMemoResult RecoverTerminalFailure(
        LilithMemoCommand command)
    {
        if (command.MemoId > 0
            && global::MemoBook.TryGet(command.MemoId, out var current))
        {
            var item = SafeDto(current);
            if (item is not null)
                return Failure(command, "official_operation_fault", item);
        }
        return Failure(command, "official_operation_fault");
    }

    private static void Append(
        Il2CppSystem.Collections.Generic.List<global::MemoEntry> nativeEntries,
        int limit,
        ICollection<LilithMemoItem> items,
        ISet<int> seen)
    {
        if (nativeEntries is null)
            return;
        for (var index = 0; index < nativeEntries.Count && items.Count < limit; index++)
        {
            var entry = nativeEntries[index];
            if (entry is not null && seen.Add(entry.id))
                items.Add(ToDto(entry));
        }
    }

    private static LilithMemoItem ToDto(global::MemoEntry entry)
    {
        var isScratch = global::MemoBook.IsScratch(entry);
        return new LilithMemoItem(
            entry.id,
            entry.text ?? string.Empty,
            isScratch ? LilithMemoKind.Scratch : LilithMemoKind.Todo,
            entry.date ?? string.Empty,
            entry.createdTime ?? string.Empty,
            entry.updatedTime ?? string.Empty,
            entry.remind,
            entry.remindHour,
            entry.remindMinute,
            ToContractRepeat(entry.repeat),
            entry.done,
            entry.doneDate ?? string.Empty,
            entry.pinned,
            entry.lastRemindedDate ?? string.Empty,
            isScratch
                ? ToContractScratchTier(entry.scratch)
                : LilithMemoScratchTier.None,
            entry.countdown);
    }

    private static LilithMemoItem? SafeDto(global::MemoEntry entry)
    {
        try
        {
            return ToDto(entry);
        }
        catch
        {
            return null;
        }
    }

    private static System.DateTime ParseManagedDate(string value)
    {
        if (!System.DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidDataException("Memo date was rejected.");
        }
        return parsed;
    }

    private static NativeNullableDateTime ParseOptionalNativeDate(string? value)
        => value is null
            ? new NativeNullableDateTime()
            : new NativeNullableDateTime(ToNativeDate(ParseManagedDate(value)));

    private static NativeDateTime ToNativeDate(System.DateTime value)
        => new(value.Year, value.Month, value.Day);

    private static global::MemoScratchTier ToNativeScratchTier(
        LilithMemoScratchTier value)
        => value switch
        {
            LilithMemoScratchTier.None => global::MemoScratchTier.None,
            LilithMemoScratchTier.Normal => global::MemoScratchTier.Normal,
            LilithMemoScratchTier.Important => global::MemoScratchTier.Important,
            LilithMemoScratchTier.Temporary => global::MemoScratchTier.Temporary,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static LilithMemoScratchTier ToContractScratchTier(int value)
        => value switch
        {
            (int)global::MemoScratchTier.None => LilithMemoScratchTier.None,
            (int)global::MemoScratchTier.Normal => LilithMemoScratchTier.Normal,
            (int)global::MemoScratchTier.Important => LilithMemoScratchTier.Important,
            (int)global::MemoScratchTier.Temporary => LilithMemoScratchTier.Temporary,
            _ => throw new InvalidDataException("Official scratch tier was rejected.")
        };

    private static global::MemoRepeat ToNativeRepeat(LilithMemoRepeat value)
        => value switch
        {
            LilithMemoRepeat.None => global::MemoRepeat.None,
            LilithMemoRepeat.Daily => global::MemoRepeat.Daily,
            LilithMemoRepeat.Weekly => global::MemoRepeat.Weekly,
            LilithMemoRepeat.Monthly => global::MemoRepeat.Monthly,
            LilithMemoRepeat.Yearly => global::MemoRepeat.Yearly,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static LilithMemoRepeat ToContractRepeat(int value)
        => value switch
        {
            (int)global::MemoRepeat.None => LilithMemoRepeat.None,
            (int)global::MemoRepeat.Daily => LilithMemoRepeat.Daily,
            (int)global::MemoRepeat.Weekly => LilithMemoRepeat.Weekly,
            (int)global::MemoRepeat.Monthly => LilithMemoRepeat.Monthly,
            (int)global::MemoRepeat.Yearly => LilithMemoRepeat.Yearly,
            _ => throw new InvalidDataException("Official memo repeat was rejected.")
        };

    private static global::MemoFilter ToNativeFilter(LilithMemoFilter value)
        => value switch
        {
            LilithMemoFilter.All => global::MemoFilter.All,
            LilithMemoFilter.Pending => global::MemoFilter.Pending,
            LilithMemoFilter.Done => global::MemoFilter.Done,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static LilithMemoResult Success(
        LilithMemoCommand command,
        params LilithMemoItem[] items)
        => new(command.OperationId, succeeded: true, "ok", items);

    private static LilithMemoResult Success(
        LilithMemoCommand command,
        IEnumerable<LilithMemoItem> items)
        => new(command.OperationId, succeeded: true, "ok", items);

    private static LilithMemoResult Failure(
        LilithMemoCommand command,
        string code,
        LilithMemoItem? item = null)
        => new(
            command.OperationId,
            succeeded: false,
            code,
            item is null ? Array.Empty<LilithMemoItem>() : new[] { item });
}
