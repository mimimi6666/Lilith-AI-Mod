using System;
using System.IO;
using System.Text.Json;

namespace LilithTextInjector;

internal enum ForegroundProactiveState
{
    Disabled = 0,
    Armed = 1,
    Dispatching = 2,
    AwaitingPlayerReply = 3
}

internal enum ForegroundProactiveDispatchAdmission
{
    Started = 0,
    NotDue = 1,
    NotArmed = 2,
    ExistingDispatch = 3
}

internal enum ForegroundProactiveDispatchOutcome
{
    ReplyCompleted = 0,
    Silent = 1,
    Abandoned = 2,
    Failed = 3
}

internal enum ForegroundProactiveStoreLoadStatus
{
    Loaded = 0,
    Missing = 1,
    RecoveredPrevious = 2,
    Corrupt = 3
}

internal interface IForegroundProactiveClock
{
    DateTimeOffset UtcNow { get; }
}

internal interface IForegroundProactiveRandom
{
    double NextUnitInterval();
}

internal sealed class SystemForegroundProactiveClock
    : IForegroundProactiveClock
{
    internal static SystemForegroundProactiveClock Instance { get; } = new();

    private SystemForegroundProactiveClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

internal sealed class SystemForegroundProactiveRandom
    : IForegroundProactiveRandom
{
    internal static SystemForegroundProactiveRandom Instance { get; } = new();

    private SystemForegroundProactiveRandom()
    {
    }

    public double NextUnitInterval() => Random.Shared.NextDouble();
}

internal sealed class ForegroundProactiveDispatchContext
{
    internal string ApplicationId { get; }
    internal string ApplicationDisplayName { get; }
    internal string ApplicationCategory { get; }
    internal long ObservedAtUnixMilliseconds { get; }
    internal string DisplayLocale { get; }

    internal ForegroundProactiveDispatchContext(
        string applicationId,
        string applicationDisplayName,
        string applicationCategory,
        long observedAtUnixMilliseconds,
        string displayLocale)
    {
        ApplicationId = ValidateSafeText(
            applicationId,
            nameof(applicationId),
            64);
        ApplicationDisplayName = ValidateSafeText(
            applicationDisplayName,
            nameof(applicationDisplayName),
            80);
        ApplicationCategory = ValidateSafeText(
            applicationCategory,
            nameof(applicationCategory),
            64);
        if (observedAtUnixMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAtUnixMilliseconds));
        }
        ObservedAtUnixMilliseconds = observedAtUnixMilliseconds;
        DisplayLocale = displayLocale switch
        {
            "zh-Hant" or "zh-Hans" or "ja" or "en" => displayLocale,
            _ => throw new ArgumentOutOfRangeException(nameof(displayLocale))
        };
    }

    private static string ValidateSafeText(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Safe application metadata is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength
            || normalized.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0
            || normalized.Contains('\\')
            || normalized.Contains('/'))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return normalized;
    }
}

internal sealed class ForegroundProactiveSnapshot
{
    internal const int CurrentSchemaVersion = 1;

    internal int SchemaVersion { get; }
    internal long Revision { get; }
    internal ForegroundProactiveState State { get; }
    internal DateTimeOffset TransitionedAtUtc { get; }
    internal DateTimeOffset? DueAtUtc { get; }
    internal string? DispatchOperationId { get; }
    internal DateTimeOffset? DispatchStartedAtUtc { get; }
    internal ForegroundProactiveDispatchContext? DispatchContext { get; }
    internal bool IsEnabled => State != ForegroundProactiveState.Disabled;

    internal ForegroundProactiveSnapshot(
        long revision,
        ForegroundProactiveState state,
        DateTimeOffset transitionedAtUtc,
        DateTimeOffset? dueAtUtc,
        string? dispatchOperationId,
        DateTimeOffset? dispatchStartedAtUtc,
        ForegroundProactiveDispatchContext? dispatchContext = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(typeof(ForegroundProactiveState), state))
            throw new ArgumentOutOfRangeException(nameof(state));

        var transitioned = NormalizeUtc(
            transitionedAtUtc,
            nameof(transitionedAtUtc));
        var due = dueAtUtc.HasValue
            ? NormalizeUtc(dueAtUtc.Value, nameof(dueAtUtc))
            : (DateTimeOffset?)null;
        var dispatchStarted = dispatchStartedAtUtc.HasValue
            ? NormalizeUtc(
                dispatchStartedAtUtc.Value,
                nameof(dispatchStartedAtUtc))
            : (DateTimeOffset?)null;
        var operation = string.IsNullOrWhiteSpace(dispatchOperationId)
            ? null
            : dispatchOperationId.Trim();
        if (operation != null && operation.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(dispatchOperationId));

        switch (state)
        {
            case ForegroundProactiveState.Disabled:
            case ForegroundProactiveState.AwaitingPlayerReply:
                if (due.HasValue || operation != null || dispatchStarted.HasValue
                    || dispatchContext != null)
                {
                    throw new ArgumentException(
                        "Disabled and awaiting states cannot carry schedule or dispatch data.");
                }
                break;
            case ForegroundProactiveState.Armed:
                if (!due.HasValue || operation != null || dispatchStarted.HasValue
                    || dispatchContext != null)
                {
                    throw new ArgumentException(
                        "Armed state requires exactly one due time.");
                }
                if (due.Value < transitioned)
                    throw new ArgumentOutOfRangeException(nameof(dueAtUtc));
                break;
            case ForegroundProactiveState.Dispatching:
                if (due.HasValue || operation == null || !dispatchStarted.HasValue
                    || dispatchContext == null)
                {
                    throw new ArgumentException(
                        "Dispatching state requires one operation identity, start time, and safe perception context.");
                }
                if (dispatchStarted.Value < transitioned)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(dispatchStartedAtUtc));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        SchemaVersion = schemaVersion;
        Revision = revision;
        State = state;
        TransitionedAtUtc = transitioned;
        DueAtUtc = due;
        DispatchOperationId = operation;
        DispatchStartedAtUtc = dispatchStarted;
        DispatchContext = dispatchContext;
    }

    internal static ForegroundProactiveSnapshot Disabled(
        DateTimeOffset nowUtc,
        long revision = 0)
        => new(
            revision,
            ForegroundProactiveState.Disabled,
            nowUtc,
            null,
            null,
            null);

    private static DateTimeOffset NormalizeUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value == default)
            throw new ArgumentOutOfRangeException(parameterName);
        return value.ToUniversalTime();
    }
}

internal sealed class ForegroundProactiveDispatchResult
{
    internal ForegroundProactiveDispatchAdmission Admission { get; }
    internal string? OperationId { get; }
    internal ForegroundProactiveDispatchContext? Context { get; }
    internal bool Started =>
        Admission == ForegroundProactiveDispatchAdmission.Started;
    internal bool RequiresReconciliation =>
        Admission == ForegroundProactiveDispatchAdmission.ExistingDispatch;

    internal ForegroundProactiveDispatchResult(
        ForegroundProactiveDispatchAdmission admission,
        string? operationId,
        ForegroundProactiveDispatchContext? context = null)
    {
        if (!Enum.IsDefined(
                typeof(ForegroundProactiveDispatchAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        var carriesOperation = admission is
            ForegroundProactiveDispatchAdmission.Started
            or ForegroundProactiveDispatchAdmission.ExistingDispatch;
        if (carriesOperation != !string.IsNullOrWhiteSpace(operationId)
            || carriesOperation != (context != null))
        {
            throw new ArgumentException(
                "Only started or recovered dispatch results carry an operation identity and context.",
                nameof(operationId));
        }
        Admission = admission;
        OperationId = operationId;
        Context = context;
    }
}

internal sealed class ForegroundProactiveStoreLoadResult
{
    internal ForegroundProactiveStoreLoadStatus Status { get; }
    internal ForegroundProactiveSnapshot? Snapshot { get; }

    internal ForegroundProactiveStoreLoadResult(
        ForegroundProactiveStoreLoadStatus status,
        ForegroundProactiveSnapshot? snapshot)
    {
        if (!Enum.IsDefined(
                typeof(ForegroundProactiveStoreLoadStatus),
                status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        var hasSnapshot = status is
            ForegroundProactiveStoreLoadStatus.Loaded
            or ForegroundProactiveStoreLoadStatus.RecoveredPrevious;
        if (hasSnapshot != (snapshot != null))
        {
            throw new ArgumentException(
                "Only loaded store results carry a snapshot.",
                nameof(snapshot));
        }
        Status = status;
        Snapshot = snapshot;
    }
}

internal interface IForegroundProactiveStateStore
{
    ForegroundProactiveStoreLoadResult Load();
    void Save(ForegroundProactiveSnapshot snapshot);
}

internal sealed class AtomicJsonForegroundProactiveStateStore
    : IForegroundProactiveStateStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _previousPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    internal AtomicJsonForegroundProactiveStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("State path cannot be empty.", nameof(path));
        _path = System.IO.Path.GetFullPath(path);
        _previousPath = _path + ".previous";
    }

    internal string Path => _path;
    internal string PreviousPath => _previousPath;

    public ForegroundProactiveStoreLoadResult Load()
    {
        lock (_gate)
        {
            var currentStatus = Read(_path, out var current);
            if (currentStatus == FileReadStatus.Loaded)
            {
                return new ForegroundProactiveStoreLoadResult(
                    ForegroundProactiveStoreLoadStatus.Loaded,
                    current);
            }

            var previousStatus = Read(_previousPath, out var previous);
            if (previousStatus == FileReadStatus.Loaded)
            {
                return new ForegroundProactiveStoreLoadResult(
                    ForegroundProactiveStoreLoadStatus.RecoveredPrevious,
                    previous);
            }

            return new ForegroundProactiveStoreLoadResult(
                currentStatus == FileReadStatus.Missing
                && previousStatus == FileReadStatus.Missing
                    ? ForegroundProactiveStoreLoadStatus.Missing
                    : ForegroundProactiveStoreLoadStatus.Corrupt,
                null);
        }
    }

    public void Save(ForegroundProactiveSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException(
                    "State path has no parent directory.");
            Directory.CreateDirectory(directory);
            var encoded = JsonSerializer.SerializeToUtf8Bytes(
                StoredState.FromDomain(snapshot),
                JsonOptions);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(encoded, 0, encoded.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(
                            temporary,
                            _path,
                            _previousPath,
                            ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(_path, _previousPath, overwrite: true);
                        if (Read(_previousPath, out _) != FileReadStatus.Loaded)
                        {
                            throw new InvalidDataException(
                                "Previous proactive state verification failed.");
                        }
                        File.Move(temporary, _path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporary, _path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static FileReadStatus Read(
        string path,
        out ForegroundProactiveSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredState>(
                File.ReadAllBytes(path),
                JsonOptions);
            snapshot = stored?.ToDomain();
            return snapshot != null
                ? FileReadStatus.Loaded
                : FileReadStatus.Invalid;
        }
        catch (FileNotFoundException)
        {
            return FileReadStatus.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return FileReadStatus.Missing;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException)
        {
            return FileReadStatus.Invalid;
        }
    }

    private enum FileReadStatus
    {
        Loaded = 0,
        Missing = 1,
        Invalid = 2
    }

    private sealed class StoredState
    {
        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public ForegroundProactiveState State { get; set; }
        public DateTimeOffset TransitionedAtUtc { get; set; }
        public DateTimeOffset? DueAtUtc { get; set; }
        public string? DispatchOperationId { get; set; }
        public DateTimeOffset? DispatchStartedAtUtc { get; set; }
        public StoredDispatchContext? DispatchContext { get; set; }

        internal static StoredState FromDomain(
            ForegroundProactiveSnapshot snapshot)
            => new()
            {
                SchemaVersion = snapshot.SchemaVersion,
                Revision = snapshot.Revision,
                State = snapshot.State,
                TransitionedAtUtc = snapshot.TransitionedAtUtc,
                DueAtUtc = snapshot.DueAtUtc,
                DispatchOperationId = snapshot.DispatchOperationId,
                DispatchStartedAtUtc = snapshot.DispatchStartedAtUtc,
                DispatchContext = snapshot.DispatchContext == null
                    ? null
                    : StoredDispatchContext.FromDomain(snapshot.DispatchContext)
            };

        internal ForegroundProactiveSnapshot ToDomain()
            => new(
                Revision,
                State,
                TransitionedAtUtc,
                DueAtUtc,
                DispatchOperationId,
                DispatchStartedAtUtc,
                DispatchContext?.ToDomain(),
                SchemaVersion);
    }

    private sealed class StoredDispatchContext
    {
        public string? ApplicationId { get; set; }
        public string? ApplicationDisplayName { get; set; }
        public string? ApplicationCategory { get; set; }
        public long ObservedAtUnixMilliseconds { get; set; }
        public string? DisplayLocale { get; set; }

        internal static StoredDispatchContext FromDomain(
            ForegroundProactiveDispatchContext context)
            => new()
            {
                ApplicationId = context.ApplicationId,
                ApplicationDisplayName = context.ApplicationDisplayName,
                ApplicationCategory = context.ApplicationCategory,
                ObservedAtUnixMilliseconds =
                    context.ObservedAtUnixMilliseconds,
                DisplayLocale = context.DisplayLocale
            };

        internal ForegroundProactiveDispatchContext ToDomain()
            => new(
                ApplicationId ?? string.Empty,
                ApplicationDisplayName ?? string.Empty,
                ApplicationCategory ?? string.Empty,
                ObservedAtUnixMilliseconds,
                DisplayLocale ?? string.Empty);
    }
}

internal sealed class ForegroundProactiveStateMachine
{
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan MaximumInterval = TimeSpan.FromMinutes(3);

    private readonly object _gate = new();
    private readonly IForegroundProactiveStateStore _store;
    private readonly IForegroundProactiveClock _clock;
    private readonly IForegroundProactiveRandom _random;
    private ForegroundProactiveSnapshot _snapshot;

    internal ForegroundProactiveStateMachine(
        IForegroundProactiveStateStore store,
        IForegroundProactiveClock clock,
        IForegroundProactiveRandom random)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _random = random ?? throw new ArgumentNullException(nameof(random));

        var loaded = _store.Load();
        LoadStatus = loaded.Status;
        _snapshot = loaded.Snapshot
            ?? ForegroundProactiveSnapshot.Disabled(NowUtc());
    }

    internal ForegroundProactiveStoreLoadStatus LoadStatus { get; }

    internal ForegroundProactiveSnapshot GetSnapshot()
    {
        lock (_gate)
            return _snapshot;
    }

    internal ForegroundProactiveSnapshot SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (enabled)
            {
                if (_snapshot.State != ForegroundProactiveState.Disabled)
                    return _snapshot;
                return Persist(ArmedAfter(NowUtc()));
            }

            if (_snapshot.State == ForegroundProactiveState.Disabled)
                return _snapshot;
            var now = NowUtc();
            return Persist(new ForegroundProactiveSnapshot(
                NextRevision(),
                ForegroundProactiveState.Disabled,
                now,
                null,
                null,
                null));
        }
    }

    internal ForegroundProactiveSnapshot RecordGenuinePlayerTurn()
    {
        lock (_gate)
        {
            if (_snapshot.State == ForegroundProactiveState.Disabled)
                return _snapshot;
            return Persist(ArmedAfter(NowUtc()));
        }
    }

    internal ForegroundProactiveDispatchResult TryBeginDueDispatch(
        ForegroundProactiveDispatchContext? context = null)
    {
        lock (_gate)
        {
            if (_snapshot.State == ForegroundProactiveState.Dispatching)
            {
                return new ForegroundProactiveDispatchResult(
                    ForegroundProactiveDispatchAdmission.ExistingDispatch,
                    _snapshot.DispatchOperationId,
                    _snapshot.DispatchContext);
            }
            if (_snapshot.State != ForegroundProactiveState.Armed)
            {
                return new ForegroundProactiveDispatchResult(
                    ForegroundProactiveDispatchAdmission.NotArmed,
                    null);
            }

            var now = NowUtc();
            if (now < _snapshot.DueAtUtc!.Value)
            {
                return new ForegroundProactiveDispatchResult(
                    ForegroundProactiveDispatchAdmission.NotDue,
                    null);
            }

            if (context == null)
            {
                throw new ArgumentNullException(
                    nameof(context),
                    "A due dispatch requires one safe perception context.");
            }

            var operationId = Guid.NewGuid().ToString("N");
            var dispatching = new ForegroundProactiveSnapshot(
                NextRevision(),
                ForegroundProactiveState.Dispatching,
                now,
                null,
                operationId,
                now,
                context);
            Persist(dispatching);
            return new ForegroundProactiveDispatchResult(
                ForegroundProactiveDispatchAdmission.Started,
                operationId,
                context);
        }
    }

    internal bool TryCompleteDispatch(
        string operationId,
        ForegroundProactiveDispatchOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return false;
        if (!Enum.IsDefined(typeof(ForegroundProactiveDispatchOutcome), outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));

        lock (_gate)
        {
            if (_snapshot.State != ForegroundProactiveState.Dispatching
                || !string.Equals(
                    _snapshot.DispatchOperationId,
                    operationId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var now = NowUtc();
            var next = outcome == ForegroundProactiveDispatchOutcome.ReplyCompleted
                ? new ForegroundProactiveSnapshot(
                    NextRevision(),
                    ForegroundProactiveState.AwaitingPlayerReply,
                    now,
                    null,
                    null,
                    null)
                : ArmedAfter(now);
            Persist(next);
            return true;
        }
    }

    private ForegroundProactiveSnapshot ArmedAfter(DateTimeOffset nowUtc)
    {
        var unit = _random.NextUnitInterval();
        if (double.IsNaN(unit) || double.IsInfinity(unit)
            || unit < 0 || unit > 1)
        {
            throw new InvalidOperationException(
                "Foreground proactive random source must return a value from zero through one.");
        }
        var rangeTicks = MaximumInterval.Ticks - MinimumInterval.Ticks;
        var randomizedTicks = (long)Math.Round(
            rangeTicks * unit,
            MidpointRounding.AwayFromZero);
        return new ForegroundProactiveSnapshot(
            NextRevision(),
            ForegroundProactiveState.Armed,
            nowUtc,
            nowUtc.Add(MinimumInterval).AddTicks(randomizedTicks),
            null,
            null);
    }

    private ForegroundProactiveSnapshot Persist(
        ForegroundProactiveSnapshot next)
    {
        _store.Save(next);
        _snapshot = next;
        return next;
    }

    private long NextRevision()
    {
        if (_snapshot.Revision == long.MaxValue)
            throw new InvalidOperationException(
                "Foreground proactive state revision is exhausted.");
        return _snapshot.Revision + 1;
    }

    private DateTimeOffset NowUtc()
    {
        var now = _clock.UtcNow;
        if (now == default)
            throw new InvalidOperationException(
                "Foreground proactive clock returned an invalid timestamp.");
        return now.ToUniversalTime();
    }
}
