using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithControlCenter;

internal enum D052HermesTaskPinStoreDisposition
{
    Ready = 1,
    Missing = 2,
    CorruptReadOnly = 3
}

internal sealed record D052HermesTaskPinLoadResult(
    IReadOnlySet<string> PinnedTaskIds,
    D052HermesTaskPinStoreDisposition Disposition)
{
    internal bool CanWrite =>
        Disposition != D052HermesTaskPinStoreDisposition.CorruptReadOnly;
}

internal interface ID052HermesTaskPinStore
{
    D052HermesTaskPinLoadResult Load();

    IReadOnlySet<string> SetTaskPin(
        string taskId,
        bool expectedIsPinned,
        bool isPinned);

    IReadOnlySet<string> RemoveDeletedTask(string taskId);
}

internal sealed class D052HermesTaskPinDocument
{
    internal const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<string> PinnedTaskIds { get; set; } = new();
}

/// <summary>
/// Stores only Control Center task-rail presentation preferences. It never
/// writes a Hermes session, transcript, title, model, or native configuration.
/// </summary>
internal sealed class D052HermesTaskPinStore : ID052HermesTaskPinStore
{
    internal const string StoreFileName = "task-pins-v1.json";
    internal const int MaximumPinnedTasks = 4096;
    internal const int MaximumTaskIdCharacters = 1024;
    internal const int MaximumStoreBytes = 512 * 1024;
    private static readonly TimeSpan MutationLockTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _gate = new();
    private readonly string _rootDirectory;
    private readonly string _storePath;
    private readonly string _mutationMutexName;

    internal string StorePath => _storePath;

    internal D052HermesTaskPinStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException(
                "Task-pin store root is required.",
                nameof(rootDirectory));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _storePath = Path.Combine(_rootDirectory, StoreFileName);
        var mutexIdentity = OperatingSystem.IsWindows()
            ? _storePath.ToUpperInvariant()
            : _storePath;
        _mutationMutexName = "LilithControlCenter.D052.TaskPins."
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mutexIdentity)));
    }

    internal static D052HermesTaskPinStore ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Local application data is unavailable.");
        }

        return new D052HermesTaskPinStore(Path.Combine(
            localApplicationData,
            "LilithControlCenter",
            "hermes-workbench-task-rail-v1"));
    }

    internal D052HermesTaskPinLoadResult Load()
    {
        lock (_gate)
            return LoadUnlocked();
    }

    internal IReadOnlySet<string> SetTaskPin(
        string taskId,
        bool expectedIsPinned,
        bool isPinned)
    {
        ValidateTaskId(taskId);
        if (expectedIsPinned == isPinned)
        {
            throw new ArgumentException(
                "Task pin mutation must change the current state.",
                nameof(isPinned));
        }

        return MutatePinnedTaskIds(pinnedTaskIds =>
        {
            var actualIsPinned = pinnedTaskIds.Contains(taskId);
            if (actualIsPinned != expectedIsPinned)
            {
                throw new InvalidOperationException(
                    "The persisted task pin state changed before the request was applied.");
            }

            if (isPinned)
            {
                if (!pinnedTaskIds.Add(taskId))
                    throw new InvalidOperationException("The task was already pinned.");
            }
            else if (!pinnedTaskIds.Remove(taskId))
            {
                throw new InvalidOperationException("The task was not pinned.");
            }

            return true;
        });
    }

    internal IReadOnlySet<string> RemoveDeletedTask(string taskId)
    {
        ValidateTaskId(taskId);
        return MutatePinnedTaskIds(pinnedTaskIds => pinnedTaskIds.Remove(taskId));
    }

    D052HermesTaskPinLoadResult ID052HermesTaskPinStore.Load() => Load();

    IReadOnlySet<string> ID052HermesTaskPinStore.SetTaskPin(
        string taskId,
        bool expectedIsPinned,
        bool isPinned)
        => SetTaskPin(taskId, expectedIsPinned, isPinned);

    IReadOnlySet<string> ID052HermesTaskPinStore.RemoveDeletedTask(string taskId)
        => RemoveDeletedTask(taskId);

    private IReadOnlySet<string> MutatePinnedTaskIds(
        Func<HashSet<string>, bool> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        lock (_gate)
        {
            using var mutationMutex = new Mutex(
                initiallyOwned: false,
                name: _mutationMutexName);
            var ownsMutex = false;
            try
            {
                try
                {
                    ownsMutex = mutationMutex.WaitOne(MutationLockTimeout);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    throw new IOException(
                        "Timed out waiting for the task-pin preference lock.");
                }

                // Re-read while holding the cross-process lock. No caller is
                // allowed to publish a cached full document over another
                // Control Center instance's newer pin mutation.
                var loaded = LoadUnlocked();
                if (!loaded.CanWrite)
                {
                    throw new InvalidOperationException(
                        "The local task-pin store is corrupt and was preserved read-only.");
                }

                var pinnedTaskIds = new HashSet<string>(
                    loaded.PinnedTaskIds,
                    StringComparer.Ordinal);
                if (mutation(pinnedTaskIds))
                    WriteUnlocked(pinnedTaskIds);
                return new HashSet<string>(pinnedTaskIds, StringComparer.Ordinal);
            }
            finally
            {
                if (ownsMutex)
                    mutationMutex.ReleaseMutex();
            }
        }
    }

    private D052HermesTaskPinLoadResult LoadUnlocked()
    {
        if (!File.Exists(_storePath))
        {
            return new D052HermesTaskPinLoadResult(
                new HashSet<string>(StringComparer.Ordinal),
                D052HermesTaskPinStoreDisposition.Missing);
        }

        try
        {
            var file = new FileInfo(_storePath);
            if (file.Length is <= 0 or > MaximumStoreBytes)
            {
                throw new InvalidDataException(
                    "Task-pin store size is invalid.");
            }

            var document = JsonSerializer.Deserialize<D052HermesTaskPinDocument>(
                File.ReadAllBytes(_storePath),
                SerializerOptions)
                ?? throw new InvalidDataException("Task-pin store is empty.");
            var pinned = Validate(document);
            return new D052HermesTaskPinLoadResult(
                pinned,
                D052HermesTaskPinStoreDisposition.Ready);
        }
        catch (Exception exception) when (exception is
            IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or ArgumentException)
        {
            // Preserve corrupt bytes for recovery. A local preference
            // failure must never rewrite or damage Hermes conversation data.
            return new D052HermesTaskPinLoadResult(
                new HashSet<string>(StringComparer.Ordinal),
                D052HermesTaskPinStoreDisposition.CorruptReadOnly);
        }
    }

    private void WriteUnlocked(IReadOnlySet<string> pinnedTaskIds)
    {
        var document = new D052HermesTaskPinDocument
        {
            PinnedTaskIds = pinnedTaskIds
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList()
        };
        _ = Validate(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            document,
            SerializerOptions);
        if (bytes.Length > MaximumStoreBytes)
            throw new InvalidOperationException("Task-pin store is too large.");

        Directory.CreateDirectory(_rootDirectory);
        var temporaryPath = Path.Combine(
            _rootDirectory,
            StoreFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateTaskId(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)
            || taskId.Length > MaximumTaskIdCharacters
            || taskId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Task ID must be a bounded identity.",
                nameof(taskId));
        }
    }

    private static HashSet<string> Validate(D052HermesTaskPinDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != D052HermesTaskPinDocument.CurrentSchemaVersion
            || document.PinnedTaskIds is null
            || document.PinnedTaskIds.Count > MaximumPinnedTasks)
        {
            throw new InvalidDataException("Task-pin store shape is invalid.");
        }

        var pinned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var taskId in document.PinnedTaskIds)
        {
            if (string.IsNullOrWhiteSpace(taskId)
                || taskId.Length > MaximumTaskIdCharacters
                || taskId.Any(char.IsControl)
                || !pinned.Add(taskId))
            {
                throw new InvalidDataException(
                    "Task-pin store contains an invalid task identity.");
            }
        }

        return pinned;
    }
}
