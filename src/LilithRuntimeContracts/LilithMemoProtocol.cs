using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

public enum LilithMemoAction
{
    List = 1,
    Get = 2,
    CreateScratch = 3,
    CreateTodo = 4,
    Update = 5,
    Delete = 6,
    Promote = 7,
    Complete = 8
}

public enum LilithMemoKind
{
    All = 0,
    Scratch = 1,
    Todo = 2
}

public enum LilithMemoScratchTier
{
    None = 0,
    Normal = 1,
    Important = 2,
    Temporary = 3
}

public enum LilithMemoRepeat
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4
}

public enum LilithMemoFilter
{
    All = 0,
    Pending = 1,
    Done = 2
}

public sealed class LilithMemoCommand
{
    public Guid OperationId { get; }
    public LilithMemoAction Action { get; }
    public int MemoId { get; }
    public string? ExpectedUpdatedTime { get; }
    public string? Text { get; }
    public string? Date { get; }
    public string? Query { get; }
    public LilithMemoKind Kind { get; }
    public LilithMemoFilter Filter { get; }
    public LilithMemoScratchTier? ScratchTier { get; }
    public bool? Remind { get; }
    public int? RemindHour { get; }
    public int? RemindMinute { get; }
    public LilithMemoRepeat? Repeat { get; }
    public bool? Done { get; }
    public bool? Pinned { get; }
    public bool? Countdown { get; }
    public int Limit { get; }

    public LilithMemoCommand(
        Guid operationId,
        LilithMemoAction action,
        int memoId = 0,
        string? expectedUpdatedTime = null,
        string? text = null,
        string? date = null,
        string? query = null,
        LilithMemoKind kind = LilithMemoKind.All,
        LilithMemoFilter filter = LilithMemoFilter.All,
        LilithMemoScratchTier? scratchTier = null,
        bool? remind = null,
        int? remindHour = null,
        int? remindMinute = null,
        LilithMemoRepeat? repeat = null,
        bool? done = null,
        bool? pinned = null,
        bool? countdown = null,
        int limit = LilithMemoProtocol.DefaultListLimit)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException(
                "A memo operation identity is required.",
                nameof(operationId));
        if (!Enum.IsDefined(typeof(LilithMemoAction), action))
            throw new ArgumentOutOfRangeException(nameof(action));
        if (!Enum.IsDefined(typeof(LilithMemoKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(LilithMemoFilter), filter))
            throw new ArgumentOutOfRangeException(nameof(filter));
        if (scratchTier.HasValue
            && !Enum.IsDefined(typeof(LilithMemoScratchTier), scratchTier.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(scratchTier));
        }
        if (scratchTier == LilithMemoScratchTier.None)
        {
            throw new ArgumentException(
                "The none scratch tier is a result marker, not a scratch mutation value.",
                nameof(scratchTier));
        }
        if (repeat.HasValue
            && !Enum.IsDefined(typeof(LilithMemoRepeat), repeat.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(repeat));
        }
        if (memoId < 0)
            throw new ArgumentOutOfRangeException(nameof(memoId));
        if (limit is < 1 or > LilithMemoProtocol.MaximumListLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (remindHour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(remindHour));
        if (remindMinute is < 0 or > 59)
            throw new ArgumentOutOfRangeException(nameof(remindMinute));
        if (remindHour.HasValue != remindMinute.HasValue)
        {
            throw new ArgumentException(
                "Reminder hour and minute must be supplied together.");
        }

        ExpectedUpdatedTime = OptionalText(
            expectedUpdatedTime,
            LilithMemoProtocol.MaximumTimestampUtf8Bytes,
            nameof(expectedUpdatedTime));
        Text = OptionalText(
            text,
            LilithMemoProtocol.MaximumTextUtf8Bytes,
            nameof(text));
        Date = OptionalDate(date, nameof(date));
        Query = OptionalText(
            query,
            LilithMemoProtocol.MaximumQueryUtf8Bytes,
            nameof(query),
            permitEmpty: true);

        var targeted = action is LilithMemoAction.Get
            or LilithMemoAction.Update
            or LilithMemoAction.Delete
            or LilithMemoAction.Promote
            or LilithMemoAction.Complete;
        if (targeted != (memoId > 0))
        {
            throw new ArgumentException(
                targeted
                    ? "The memo operation requires a positive memo id."
                    : "The memo operation does not accept a memo id.",
                nameof(memoId));
        }
        var mutatesExisting = action is LilithMemoAction.Update
            or LilithMemoAction.Delete
            or LilithMemoAction.Promote
            or LilithMemoAction.Complete;
        if (mutatesExisting && ExpectedUpdatedTime is null)
        {
            throw new ArgumentException(
                "An expected updated-time is required before changing an existing memo.",
                nameof(expectedUpdatedTime));
        }
        if (!mutatesExisting && ExpectedUpdatedTime is not null)
        {
            throw new ArgumentException(
                "This memo operation does not accept an expected updated-time.",
                nameof(expectedUpdatedTime));
        }
        if (action is LilithMemoAction.CreateScratch or LilithMemoAction.CreateTodo)
        {
            if (Text is null)
                throw new ArgumentException("New memos require text.", nameof(text));
        }
        else if (action != LilithMemoAction.Update && Text is not null)
        {
            throw new ArgumentException(
                "This memo operation does not accept text.",
                nameof(text));
        }
        if (action == LilithMemoAction.Update
            && Text is null
            && Date is null
            && !scratchTier.HasValue
            && !remind.HasValue
            && !remindHour.HasValue
            && !repeat.HasValue
            && !pinned.HasValue
            && !countdown.HasValue)
        {
            throw new ArgumentException(
                "A memo update requires at least one changed field.");
        }
        if (action == LilithMemoAction.Complete && !done.HasValue)
            throw new ArgumentException("Complete requires a done value.", nameof(done));
        if (action != LilithMemoAction.Complete && done.HasValue)
        {
            throw new ArgumentException(
                "Done state is changed only by the complete action.",
                nameof(done));
        }
        if (action != LilithMemoAction.List
            && (Query is not null
                || kind != LilithMemoKind.All
                || filter != LilithMemoFilter.All
                || limit != LilithMemoProtocol.DefaultListLimit))
        {
            throw new ArgumentException(
                "List filters are accepted only by the list action.");
        }
        if (action == LilithMemoAction.CreateScratch
            && scratchTier is null)
        {
            scratchTier = LilithMemoScratchTier.Normal;
        }
        if (action is not (LilithMemoAction.CreateScratch or LilithMemoAction.Update)
            && scratchTier.HasValue)
        {
            throw new ArgumentException(
                "Scratch tier is accepted only when creating or updating a scratch memo.",
                nameof(scratchTier));
        }
        if (action is not (LilithMemoAction.CreateTodo
                or LilithMemoAction.Update
                or LilithMemoAction.Promote
                or LilithMemoAction.Complete)
            && Date is not null)
        {
            throw new ArgumentException(
                "A date is accepted only when creating, updating, promoting, or completing a todo.");
        }
        if (action is not (LilithMemoAction.CreateTodo or LilithMemoAction.Update)
            && (remind.HasValue
                || remindHour.HasValue
                || repeat.HasValue
                || pinned.HasValue
                || countdown.HasValue))
        {
            throw new ArgumentException(
                "Todo scheduling fields are accepted only when creating or updating a todo.");
        }
        if (action == LilithMemoAction.CreateTodo
            && remind == true
            && countdown == true)
        {
            throw new ArgumentException(
                "A new todo cannot enable reminder and countdown together.");
        }

        OperationId = operationId;
        Action = action;
        MemoId = memoId;
        Kind = kind;
        Filter = filter;
        ScratchTier = scratchTier;
        Remind = remind;
        RemindHour = remindHour;
        RemindMinute = remindMinute;
        Repeat = repeat;
        Done = done;
        Pinned = pinned;
        Countdown = countdown;
        Limit = limit;
    }

    private static string? OptionalText(
        string? value,
        int maximumUtf8Bytes,
        string parameterName,
        bool permitEmpty = false)
    {
        if (value is null)
            return null;
        if (value.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(value) > maximumUtf8Bytes
            || (!permitEmpty && string.IsNullOrWhiteSpace(value)))
        {
            throw new ArgumentException(
                "Memo text was empty or exceeded its bound.",
                parameterName);
        }
        return value;
    }

    private static string? OptionalDate(string? value, string parameterName)
    {
        if (value is null)
            return null;
        if (!System.DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
        {
            throw new ArgumentException(
                "Memo dates must use yyyy-MM-dd.",
                parameterName);
        }
        return value;
    }
}

public sealed class LilithMemoItem
{
    public int MemoId { get; }
    public string Text { get; }
    public LilithMemoKind Kind { get; }
    public string Date { get; }
    public string CreatedTime { get; }
    public string UpdatedTime { get; }
    public bool Remind { get; }
    public int RemindHour { get; }
    public int RemindMinute { get; }
    public LilithMemoRepeat Repeat { get; }
    public bool Done { get; }
    public string DoneDate { get; }
    public bool Pinned { get; }
    public string LastRemindedDate { get; }
    public LilithMemoScratchTier ScratchTier { get; }
    public bool Countdown { get; }

    public LilithMemoItem(
        int memoId,
        string text,
        LilithMemoKind kind,
        string date,
        string createdTime,
        string updatedTime,
        bool remind,
        int remindHour,
        int remindMinute,
        LilithMemoRepeat repeat,
        bool done,
        string doneDate,
        bool pinned,
        string lastRemindedDate,
        LilithMemoScratchTier scratchTier,
        bool countdown)
    {
        if (memoId <= 0)
            throw new ArgumentOutOfRangeException(nameof(memoId));
        if (kind is not (LilithMemoKind.Scratch or LilithMemoKind.Todo))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(LilithMemoRepeat), repeat))
            throw new ArgumentOutOfRangeException(nameof(repeat));
        if (!Enum.IsDefined(typeof(LilithMemoScratchTier), scratchTier))
            throw new ArgumentOutOfRangeException(nameof(scratchTier));
        if (remindHour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(remindHour));
        if (remindMinute is < 0 or > 59)
            throw new ArgumentOutOfRangeException(nameof(remindMinute));
        MemoId = memoId;
        Text = RequiredBounded(text, LilithMemoProtocol.MaximumTextUtf8Bytes, nameof(text));
        Kind = kind;
        Date = Bounded(date, LilithMemoProtocol.MaximumTimestampUtf8Bytes, nameof(date));
        CreatedTime = Bounded(createdTime, LilithMemoProtocol.MaximumTimestampUtf8Bytes, nameof(createdTime));
        UpdatedTime = RequiredBounded(updatedTime, LilithMemoProtocol.MaximumTimestampUtf8Bytes, nameof(updatedTime));
        Remind = remind;
        RemindHour = remindHour;
        RemindMinute = remindMinute;
        Repeat = repeat;
        Done = done;
        DoneDate = Bounded(doneDate, LilithMemoProtocol.MaximumTimestampUtf8Bytes, nameof(doneDate));
        Pinned = pinned;
        LastRemindedDate = Bounded(lastRemindedDate, LilithMemoProtocol.MaximumTimestampUtf8Bytes, nameof(lastRemindedDate));
        ScratchTier = scratchTier;
        Countdown = countdown;
    }

    private static string RequiredBounded(string value, int maximum, string name)
    {
        var result = Bounded(value, maximum, name);
        if (string.IsNullOrWhiteSpace(result))
            throw new ArgumentException("Memo item text was empty.", name);
        return result;
    }

    private static string Bounded(string? value, int maximum, string name)
    {
        value ??= string.Empty;
        if (value.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(value) > maximum)
            throw new ArgumentException("Memo item text exceeded its bound.", name);
        return value;
    }
}

public sealed class LilithMemoResult
{
    private readonly IReadOnlyList<LilithMemoItem> _items;

    public Guid OperationId { get; }
    public bool Succeeded { get; }
    public string Code { get; }
    public IReadOnlyList<LilithMemoItem> Items => _items;

    public LilithMemoResult(
        Guid operationId,
        bool succeeded,
        string code,
        IEnumerable<LilithMemoItem>? items = null)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("A memo operation identity is required.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(code)
            || code.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(code) > LilithMemoProtocol.MaximumCodeUtf8Bytes)
        {
            throw new ArgumentException("A bounded memo result code is required.", nameof(code));
        }
        if (succeeded != string.Equals(code, "ok", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Memo success and the exact ok code must agree.",
                nameof(code));
        }
        var values = (items ?? Array.Empty<LilithMemoItem>()).ToArray();
        if (values.Length > LilithMemoProtocol.MaximumListLimit
            || values.Any(static item => item is null)
            || values.Select(static item => item.MemoId).Distinct().Count() != values.Length)
        {
            throw new ArgumentException("Memo result items were rejected.", nameof(items));
        }
        OperationId = operationId;
        Succeeded = succeeded;
        Code = code;
        _items = Array.AsReadOnly(values);
    }
}

public static class LilithMemoProtocol
{
    public const int Version = 1;
    public const int DefaultListLimit = 25;
    public const int MaximumListLimit = 50;
    public const int MaximumTextUtf8Bytes = 32 * 1024;
    public const int MaximumQueryUtf8Bytes = 512;
    public const int MaximumTimestampUtf8Bytes = 128;
    public const int MaximumCodeUtf8Bytes = 256;
    public const int MaximumFrameBytes = 512 * 1024;
    public const int MaximumJsonDepth = 8;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    public static byte[] EncodeCommand(LilithMemoCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Encode(writer => WriteCommand(writer, command));
    }

    public static byte[] EncodeResult(LilithMemoResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Encode(writer => WriteResult(writer, result));
    }

    public static LilithMemoCommand DecodeCommand(ReadOnlyMemory<byte> frame)
    {
        using var document = Parse(frame);
        return ReadCommand(document.RootElement);
    }

    public static LilithMemoResult DecodeResult(ReadOnlyMemory<byte> frame)
    {
        using var document = Parse(frame);
        return ReadResult(document.RootElement);
    }

    public static LilithMemoCommand DecodeArguments(
        JsonElement arguments,
        Guid operationId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(arguments);
        if (bytes.Length > MaximumFrameBytes)
            throw new InvalidDataException("Memo arguments exceeded their bound.");
        using var document = JsonDocument.Parse(bytes, DocumentOptions);
        return ReadCommandBody(document.RootElement, operationId, hasEnvelope: false);
    }

    public static async Task WriteCommandAsync(
        Stream stream,
        LilithMemoCommand command,
        CancellationToken cancellationToken = default)
        => await WriteFrameAsync(stream, EncodeCommand(command), cancellationToken)
            .ConfigureAwait(false);

    public static async Task WriteResultAsync(
        Stream stream,
        LilithMemoResult result,
        CancellationToken cancellationToken = default)
        => await WriteFrameAsync(stream, EncodeResult(result), cancellationToken)
            .ConfigureAwait(false);

    public static async Task<LilithMemoCommand> ReadCommandAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
        => DecodeCommand(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false));

    public static async Task<LilithMemoResult> ReadResultAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
        => DecodeResult(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false));

    internal static void WriteCommand(Utf8JsonWriter writer, LilithMemoCommand command)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", Version);
        writer.WriteString("kind", "memo.command");
        writer.WriteString("operation_id", command.OperationId.ToString("N"));
        writer.WriteString("action", ActionToken(command.Action));
        WriteCommandBody(writer, command);
        writer.WriteEndObject();
    }

    internal static void WriteResult(Utf8JsonWriter writer, LilithMemoResult result)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", Version);
        writer.WriteString("kind", "memo.result");
        writer.WriteString("operation_id", result.OperationId.ToString("N"));
        writer.WriteBoolean("succeeded", result.Succeeded);
        writer.WriteString("code", result.Code);
        writer.WritePropertyName("items");
        writer.WriteStartArray();
        foreach (var item in result.Items)
            WriteItem(writer, item);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCommandBody(Utf8JsonWriter writer, LilithMemoCommand command)
    {
        if (command.MemoId > 0)
            writer.WriteNumber("memo_id", command.MemoId);
        WriteOptional(writer, "expected_updated_time", command.ExpectedUpdatedTime);
        WriteOptional(writer, "text", command.Text);
        WriteOptional(writer, "date", command.Date);
        WriteOptional(writer, "query", command.Query);
        if (command.Action == LilithMemoAction.List)
        {
            writer.WriteString("memo_kind", KindToken(command.Kind));
            writer.WriteString("filter", FilterToken(command.Filter));
            writer.WriteNumber("limit", command.Limit);
        }
        if (command.ScratchTier.HasValue)
            writer.WriteString("scratch_tier", ScratchTierToken(command.ScratchTier.Value));
        WriteOptional(writer, "remind", command.Remind);
        WriteOptional(writer, "remind_hour", command.RemindHour);
        WriteOptional(writer, "remind_minute", command.RemindMinute);
        if (command.Repeat.HasValue)
            writer.WriteString("repeat", RepeatToken(command.Repeat.Value));
        WriteOptional(writer, "done", command.Done);
        WriteOptional(writer, "pinned", command.Pinned);
        WriteOptional(writer, "countdown", command.Countdown);
    }

    private static void WriteItem(Utf8JsonWriter writer, LilithMemoItem item)
    {
        writer.WriteStartObject();
        writer.WriteNumber("memo_id", item.MemoId);
        writer.WriteString("text", item.Text);
        writer.WriteString("memo_kind", KindToken(item.Kind));
        writer.WriteString("date", item.Date);
        writer.WriteString("created_time", item.CreatedTime);
        writer.WriteString("updated_time", item.UpdatedTime);
        writer.WriteBoolean("remind", item.Remind);
        writer.WriteNumber("remind_hour", item.RemindHour);
        writer.WriteNumber("remind_minute", item.RemindMinute);
        writer.WriteString("repeat", RepeatToken(item.Repeat));
        writer.WriteBoolean("done", item.Done);
        writer.WriteString("done_date", item.DoneDate);
        writer.WriteBoolean("pinned", item.Pinned);
        writer.WriteString("last_reminded_date", item.LastRemindedDate);
        writer.WriteString("scratch_tier", ScratchTierToken(item.ScratchTier));
        writer.WriteBoolean("countdown", item.Countdown);
        writer.WriteEndObject();
    }

    internal static LilithMemoCommand ReadCommand(JsonElement root)
    {
        RequireObject(root);
        RequireProperty(root, "version", JsonValueKind.Number);
        RequireVersion(root);
        if (RequiredString(root, "kind") != "memo.command")
            throw new InvalidDataException("Memo command kind was rejected.");
        var operationId = RequiredGuid(root, "operation_id");
        return ReadCommandBody(root, operationId, hasEnvelope: true);
    }

    private static LilithMemoCommand ReadCommandBody(
        JsonElement root,
        Guid operationId,
        bool hasEnvelope)
    {
        RequireObject(root);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "action", "memo_id", "expected_updated_time", "text", "date",
            "query", "memo_kind", "filter", "scratch_tier",
            "remind", "remind_hour", "remind_minute", "repeat", "done",
            "pinned", "countdown", "limit"
        };
        if (hasEnvelope)
        {
            allowed.Add("version");
            allowed.Add("kind");
            allowed.Add("operation_id");
        }
        RequireAllowedUnique(root, allowed);
        var action = ParseAction(RequiredString(root, "action"));
        var actionFields = AllowedCommandBodyFields(action);
        foreach (var property in root.EnumerateObject())
        {
            if (!hasEnvelope || property.Name is not ("version" or "kind" or "operation_id"))
            {
                if (!actionFields.Contains(property.Name))
                {
                    throw new InvalidDataException(
                        "Memo action received an unsupported property.");
                }
            }
        }
        return new LilithMemoCommand(
            operationId,
            action,
            OptionalInt32(root, "memo_id") ?? 0,
            OptionalString(root, "expected_updated_time"),
            OptionalString(root, "text"),
            OptionalString(root, "date"),
            OptionalString(root, "query"),
            ParseKind(OptionalString(root, "memo_kind") ?? "all"),
            ParseFilter(OptionalString(root, "filter") ?? "all"),
            OptionalString(root, "scratch_tier") is { } tier
                ? ParseScratchTier(tier)
                : null,
            OptionalBoolean(root, "remind"),
            OptionalInt32(root, "remind_hour"),
            OptionalInt32(root, "remind_minute"),
            OptionalString(root, "repeat") is { } repeat
                ? ParseRepeat(repeat)
                : null,
            OptionalBoolean(root, "done"),
            OptionalBoolean(root, "pinned"),
            OptionalBoolean(root, "countdown"),
            OptionalInt32(root, "limit") ?? DefaultListLimit);
    }

    internal static LilithMemoResult ReadResult(JsonElement root)
    {
        RequireExact(root, "version", "kind", "operation_id", "succeeded", "code", "items");
        RequireVersion(root);
        if (RequiredString(root, "kind") != "memo.result")
            throw new InvalidDataException("Memo result kind was rejected.");
        var itemsElement = root.GetProperty("items");
        if (itemsElement.ValueKind != JsonValueKind.Array
            || itemsElement.GetArrayLength() > MaximumListLimit)
        {
            throw new InvalidDataException("Memo result items were rejected.");
        }
        var items = new List<LilithMemoItem>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            RequireExact(
                item,
                "memo_id", "text", "memo_kind", "date", "created_time",
                "updated_time", "remind", "remind_hour", "remind_minute",
                "repeat", "done", "done_date", "pinned", "last_reminded_date",
                "scratch_tier", "countdown");
            items.Add(new LilithMemoItem(
                RequiredInt32(item, "memo_id"),
                RequiredString(item, "text"),
                ParseKind(RequiredString(item, "memo_kind")),
                RequiredString(item, "date"),
                RequiredString(item, "created_time"),
                RequiredString(item, "updated_time"),
                RequiredBoolean(item, "remind"),
                RequiredInt32(item, "remind_hour"),
                RequiredInt32(item, "remind_minute"),
                ParseRepeat(RequiredString(item, "repeat")),
                RequiredBoolean(item, "done"),
                RequiredString(item, "done_date"),
                RequiredBoolean(item, "pinned"),
                RequiredString(item, "last_reminded_date"),
                ParseScratchTier(RequiredString(item, "scratch_tier")),
                RequiredBoolean(item, "countdown")));
        }
        return new LilithMemoResult(
            RequiredGuid(root, "operation_id"),
            RequiredBoolean(root, "succeeded"),
            RequiredString(root, "code"),
            items);
    }

    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            write(writer);
        if (stream.Length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Memo frame length was rejected.");
        return stream.ToArray();
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Memo frame length was rejected.");
        var span = frame.Span;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            throw new InvalidDataException("Memo frame byte-order mark was rejected.");
        try
        {
            return JsonDocument.Parse(frame, DocumentOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Memo frame JSON was rejected.", exception);
        }
    }

    private static async Task WriteFrameAsync(
        Stream stream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Memo stream must be writable.", nameof(stream));
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, frame.Length);
        await stream.WriteAsync(prefix.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Memo stream must be readable.", nameof(stream));
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Memo frame prefix was rejected.");
        var frame = new byte[length];
        await ReadExactlyAsync(stream, frame, cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Memo frame ended early.");
            offset += read;
        }
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value.HasValue)
            writer.WriteBoolean(name, value.Value);
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, int? value)
    {
        if (value.HasValue)
            writer.WriteNumber(name, value.Value);
    }

    private static void RequireObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Memo JSON object was rejected.");
    }

    private static void RequireExact(JsonElement root, params string[] names)
    {
        RequireAllowedUnique(root, new HashSet<string>(names, StringComparer.Ordinal));
        if (root.EnumerateObject().Count() != names.Length)
            throw new InvalidDataException("Memo JSON property was missing.");
    }

    private static void RequireAllowedUnique(JsonElement root, HashSet<string> allowed)
    {
        RequireObject(root);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("Memo JSON property was rejected.");
        }
    }

    private static void RequireProperty(JsonElement root, string name, JsonValueKind kind)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != kind)
            throw new InvalidDataException("Memo JSON property was missing or invalid.");
    }

    private static void RequireVersion(JsonElement root)
    {
        if (!root.GetProperty("version").TryGetInt32(out var version) || version != Version)
            throw new InvalidDataException("Memo protocol version was rejected.");
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Memo JSON string was rejected.");
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Memo JSON string was rejected.");
        return value.GetString();
    }

    private static int RequiredInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException("Memo JSON integer was rejected.");
        }
        return result;
    }

    private static int? OptionalInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException("Memo JSON integer was rejected.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Memo JSON boolean was rejected.");
        }
        return value.GetBoolean();
    }

    private static bool? OptionalBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Memo JSON boolean was rejected.");
        return value.GetBoolean();
    }

    private static Guid RequiredGuid(JsonElement root, string name)
    {
        var text = RequiredString(root, name);
        if (!IsLowerHexIdentity(text)
            || !Guid.TryParseExact(text, "N", out var value)
            || value == Guid.Empty)
        {
            throw new InvalidDataException("Memo operation identity was rejected.");
        }
        return value;
    }

    private static HashSet<string> AllowedCommandBodyFields(LilithMemoAction action)
        => new(
            action switch
            {
                LilithMemoAction.List => new[]
                {
                    "action", "query", "memo_kind", "filter", "limit"
                },
                LilithMemoAction.Get => new[] { "action", "memo_id" },
                LilithMemoAction.CreateScratch => new[]
                {
                    "action", "text", "scratch_tier"
                },
                LilithMemoAction.CreateTodo => new[]
                {
                    "action", "text", "date", "remind", "remind_hour",
                    "remind_minute", "repeat", "pinned", "countdown"
                },
                LilithMemoAction.Update => new[]
                {
                    "action", "memo_id", "expected_updated_time", "text",
                    "date", "scratch_tier", "remind", "remind_hour",
                    "remind_minute", "repeat", "pinned", "countdown"
                },
                LilithMemoAction.Delete => new[]
                {
                    "action", "memo_id", "expected_updated_time"
                },
                LilithMemoAction.Promote => new[]
                {
                    "action", "memo_id", "expected_updated_time", "date"
                },
                LilithMemoAction.Complete => new[]
                {
                    "action", "memo_id", "expected_updated_time", "done", "date"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            },
            StringComparer.Ordinal);

    private static bool IsLowerHexIdentity(string value)
        => value.Length == 32
            && value.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string ActionToken(LilithMemoAction value) => value switch
    {
        LilithMemoAction.List => "list",
        LilithMemoAction.Get => "get",
        LilithMemoAction.CreateScratch => "create_scratch",
        LilithMemoAction.CreateTodo => "create_todo",
        LilithMemoAction.Update => "update",
        LilithMemoAction.Delete => "delete",
        LilithMemoAction.Promote => "promote",
        LilithMemoAction.Complete => "complete",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static LilithMemoAction ParseAction(string value) => value switch
    {
        "list" => LilithMemoAction.List,
        "get" => LilithMemoAction.Get,
        "create_scratch" => LilithMemoAction.CreateScratch,
        "create_todo" => LilithMemoAction.CreateTodo,
        "update" => LilithMemoAction.Update,
        "delete" => LilithMemoAction.Delete,
        "promote" => LilithMemoAction.Promote,
        "complete" => LilithMemoAction.Complete,
        _ => throw new InvalidDataException("Memo action was rejected.")
    };

    public static string KindToken(LilithMemoKind value) => value switch
    {
        LilithMemoKind.All => "all",
        LilithMemoKind.Scratch => "scratch",
        LilithMemoKind.Todo => "todo",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static LilithMemoKind ParseKind(string value) => value switch
    {
        "all" => LilithMemoKind.All,
        "scratch" => LilithMemoKind.Scratch,
        "todo" => LilithMemoKind.Todo,
        _ => throw new InvalidDataException("Memo kind was rejected.")
    };

    public static string ScratchTierToken(LilithMemoScratchTier value) => value switch
    {
        LilithMemoScratchTier.None => "none",
        LilithMemoScratchTier.Normal => "normal",
        LilithMemoScratchTier.Important => "important",
        LilithMemoScratchTier.Temporary => "temporary",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static LilithMemoScratchTier ParseScratchTier(string value) => value switch
    {
        "none" => LilithMemoScratchTier.None,
        "normal" => LilithMemoScratchTier.Normal,
        "important" => LilithMemoScratchTier.Important,
        "temporary" => LilithMemoScratchTier.Temporary,
        _ => throw new InvalidDataException("Memo scratch tier was rejected.")
    };

    public static string RepeatToken(LilithMemoRepeat value) => value switch
    {
        LilithMemoRepeat.None => "none",
        LilithMemoRepeat.Daily => "daily",
        LilithMemoRepeat.Weekly => "weekly",
        LilithMemoRepeat.Monthly => "monthly",
        LilithMemoRepeat.Yearly => "yearly",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static LilithMemoRepeat ParseRepeat(string value) => value switch
    {
        "none" => LilithMemoRepeat.None,
        "daily" => LilithMemoRepeat.Daily,
        "weekly" => LilithMemoRepeat.Weekly,
        "monthly" => LilithMemoRepeat.Monthly,
        "yearly" => LilithMemoRepeat.Yearly,
        _ => throw new InvalidDataException("Memo repeat was rejected.")
    };

    public static string FilterToken(LilithMemoFilter value) => value switch
    {
        LilithMemoFilter.All => "all",
        LilithMemoFilter.Pending => "pending",
        LilithMemoFilter.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static LilithMemoFilter ParseFilter(string value) => value switch
    {
        "all" => LilithMemoFilter.All,
        "pending" => LilithMemoFilter.Pending,
        "done" => LilithMemoFilter.Done,
        _ => throw new InvalidDataException("Memo filter was rejected.")
    };
}
