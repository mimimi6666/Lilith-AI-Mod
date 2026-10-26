using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LilithPermissionControl;

internal enum D045PermissionCapability
{
    SanitizedLilithStatusRead = 1,
    PublicHttpsUrlFetch = 2,
    ClipboardRead = 3,
    LocalFileRead = 4,
    LocalFolderEnumerate = 5,
    ScreenshotCapture = 6,
    ScreenshotUpload = 7,
    LocalContentExternalShare = 8,
    GracefulProcessClose = 9,
    ForceTerminateProcess = 10,
    RecycleBinDelete = 11,
    PermanentDelete = 12,
    Shutdown = 13,
    Restart = 14,
    RawShellArbitraryCommand = 15,
    ArbitraryExternalAppInputKeystrokes = 16,
    GeneralElevationAdminAccess = 17,
    LocalDataCreate = 18,
    ApplicationLaunch = 19,
    LocalContentOpen = 20,
    ClipboardWrite = 21,
    SystemDiagnosticsRead = 22,
    ApplicationInventoryRead = 23,
    ProcessInventoryRead = 24,
    WindowInventoryRead = 25,
    ExternalAppSemanticUiInspect = 26,
    LocalPathSearch = 27,
    LocalContentSearch = 28,
    FileModify = 29,
    LocalContentCopy = 30,
    LocalContentMoveRename = 31,
    FolderTreeCreate = 32,
    ArchiveManage = 33,
    WindowControl = 34,
    ExternalAppSemanticUiAction = 35,
    ExternalAppPointerInput = 36,
    ApplicationLaunchWithArguments = 37,
    ProcessExecuteNonInteractive = 38,
    ProcessBackgroundManage = 39,
    WebPublicSearch = 40,
    BrowserNavigate = 41,
    DownloadPublicHttps = 42
}

internal sealed class D045PermissionControlSnapshot
{
    internal const int CurrentSchemaVersion = 5;
    internal const int SchemaFourVersion = 4;
    internal const int SchemaFourCapabilityCount = 20;
    internal const int SchemaThreeVersion = 3;
    internal const int SchemaThreeCapabilityCount = 19;
    internal const int SchemaTwoVersion = 2;
    internal const int SchemaTwoCapabilityCount = 17;
    internal const int ExactCapabilityCount = 42;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; } = 1;
    public bool[] Enabled { get; set; } = new bool[ExactCapabilityCount];
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UnixEpoch;

    internal bool IsEnabled(D045PermissionCapability capability)
    {
        DemandCapability(capability);
        return Enabled[(int)capability - 1];
    }

    internal int EnabledCount => Enabled.Count(value => value);
    internal bool AnyEnabled => EnabledCount > 0;

    internal D045PermissionControlSnapshot Copy()
        => new()
        {
            SchemaVersion = SchemaVersion,
            Revision = Revision,
            Enabled = (bool[])Enabled.Clone(),
            UpdatedUtc = UpdatedUtc
        };

    internal static void DemandCapability(D045PermissionCapability capability)
    {
        if ((int)capability is < 1 or > ExactCapabilityCount)
            throw new ArgumentOutOfRangeException(nameof(capability));
    }
}

internal enum D045PermissionStoreDisposition
{
    Ready = 1,
    MissingDefaultOff = 2,
    InvalidDefaultOff = 3,
    LegacyScreenshotMigrated = 4,
    PreviousSchemaMigrated = 5
}

internal sealed class D045PermissionLoadResult
{
    internal D045PermissionControlSnapshot Snapshot { get; }
    internal D045PermissionStoreDisposition Disposition { get; }

    internal D045PermissionLoadResult(
        D045PermissionControlSnapshot snapshot,
        D045PermissionStoreDisposition disposition)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Disposition = disposition;
    }
}

/// <summary>
/// Player-owned desired permission state shared by Control Center, the MOD and
/// the Local Task Harness. Missing, malformed or unknown state is always
/// interpreted as all capabilities being Default Off.
/// </summary>
internal sealed class D045PermissionControlStore
{
    internal const string StoreFileName = "permissions-v2.json";
    internal const string LegacyStoreFileName = "permissions-v1.json";
    internal const int MaximumStoreBytes = 32 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly string _storePath;
    private readonly string _legacyStorePath;
    private readonly object _gate = new();

    internal string StorePath => _storePath;

    internal D045PermissionControlStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Permission root is required.", nameof(rootDirectory));

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _storePath = Path.Combine(_rootDirectory, StoreFileName);
        _legacyStorePath = Path.Combine(_rootDirectory, LegacyStoreFileName);
    }

    internal static D045PermissionControlStore ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("Local application data is unavailable.");

        return new D045PermissionControlStore(Path.Combine(
            localApplicationData,
            "LilithControlCenter",
            "permissions-v1"));
    }

    internal D045PermissionLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_storePath))
                return TryLoadLegacyScreenshot();

            try
            {
                var file = new FileInfo(_storePath);
                if (file.Length <= 0 || file.Length > MaximumStoreBytes)
                    throw new InvalidDataException("Permission store size is invalid.");

                var snapshot = JsonSerializer.Deserialize<D045PermissionControlSnapshot>(
                    File.ReadAllBytes(_storePath),
                    SerializerOptions)
                    ?? throw new InvalidDataException("Permission store is empty.");
                if (IsSchemaFourLegacyShape(snapshot))
                {
                    DemandLegacySnapshot(
                        snapshot,
                        D045PermissionControlSnapshot.SchemaFourVersion,
                        D045PermissionControlSnapshot.SchemaFourCapabilityCount);
                    return new D045PermissionLoadResult(
                        MigrateLegacySnapshot(snapshot),
                        D045PermissionStoreDisposition.PreviousSchemaMigrated);
                }
                if (IsSchemaThreeLegacyShape(snapshot))
                {
                    DemandLegacySnapshot(
                        snapshot,
                        D045PermissionControlSnapshot.SchemaThreeVersion,
                        D045PermissionControlSnapshot.SchemaThreeCapabilityCount);
                    return new D045PermissionLoadResult(
                        MigrateLegacySnapshot(snapshot),
                        D045PermissionStoreDisposition.PreviousSchemaMigrated);
                }
                if (IsSchemaTwoLegacyShape(snapshot))
                {
                    DemandLegacySnapshot(
                        snapshot,
                        D045PermissionControlSnapshot.SchemaTwoVersion,
                        D045PermissionControlSnapshot.SchemaTwoCapabilityCount);
                    return new D045PermissionLoadResult(
                        MigrateLegacySnapshot(snapshot),
                        D045PermissionStoreDisposition.PreviousSchemaMigrated);
                }
                DemandSnapshot(snapshot);
                return new D045PermissionLoadResult(
                    snapshot.Copy(),
                    D045PermissionStoreDisposition.Ready);
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return new D045PermissionLoadResult(
                    DefaultOff(),
                    D045PermissionStoreDisposition.InvalidDefaultOff);
            }
        }
    }

    internal D045PermissionControlSnapshot Set(
        D045PermissionCapability capability,
        bool enabled,
        DateTimeOffset now)
    {
        return SetMany(
            new[]
            {
                new KeyValuePair<D045PermissionCapability, bool>(
                    capability,
                    enabled)
            },
            now);
    }

    /// <summary>
    /// Applies a UI batch to independent leaf permissions as one revision and
    /// one atomic store replacement. The batch never creates a category,
    /// wildcard or inherited runtime authority.
    /// </summary>
    internal D045PermissionControlSnapshot SetMany(
        IEnumerable<KeyValuePair<D045PermissionCapability, bool>> changes,
        DateTimeOffset now)
    {
        if (changes is null)
            throw new ArgumentNullException(nameof(changes));

        var materialized = changes.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one leaf permission is required.", nameof(changes));

        var seen = new HashSet<D045PermissionCapability>();
        foreach (var change in materialized)
        {
            D045PermissionControlSnapshot.DemandCapability(change.Key);
            if (!seen.Add(change.Key))
                throw new ArgumentException("Duplicate leaf permission in batch.", nameof(changes));
        }

        lock (_gate)
        {
            var current = Load().Snapshot;
            var next = current.Copy();
            next.SchemaVersion = D045PermissionControlSnapshot.CurrentSchemaVersion;
            next.Revision = NextRevision(current.Revision);
            foreach (var change in materialized)
                next.Enabled[(int)change.Key - 1] = change.Value;
            next.UpdatedUtc = now.ToUniversalTime();
            Save(next);
            return next.Copy();
        }
    }

    internal D045PermissionControlSnapshot EmergencyStop(DateTimeOffset now)
    {
        lock (_gate)
        {
            var current = Load().Snapshot;
            var next = DefaultOff();
            next.Revision = NextRevision(current.Revision);
            next.UpdatedUtc = now.ToUniversalTime();
            Save(next);
            return next.Copy();
        }
    }

    private D045PermissionLoadResult TryLoadLegacyScreenshot()
    {
        if (!File.Exists(_legacyStorePath))
        {
            return new D045PermissionLoadResult(
                DefaultOff(),
                D045PermissionStoreDisposition.MissingDefaultOff);
        }

        try
        {
            var file = new FileInfo(_legacyStorePath);
            if (file.Length <= 0 || file.Length > 16 * 1024)
                throw new InvalidDataException("Legacy permission store size is invalid.");
            var legacy = JsonSerializer.Deserialize<D045LegacyPermissionSnapshot>(
                File.ReadAllBytes(_legacyStorePath),
                SerializerOptions)
                ?? throw new InvalidDataException("Legacy permission store is empty.");
            if (legacy.SchemaVersion != 1 || legacy.Revision <= 0)
                throw new InvalidDataException("Legacy permission store is invalid.");

            var migrated = DefaultOff();
            migrated.Revision = legacy.Revision;
            migrated.Enabled[(int)D045PermissionCapability.ScreenshotCapture - 1] =
                legacy.ScreenshotCaptureEnabled;
            migrated.UpdatedUtc = legacy.UpdatedUtc.ToUniversalTime();
            return new D045PermissionLoadResult(
                migrated,
                D045PermissionStoreDisposition.LegacyScreenshotMigrated);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return new D045PermissionLoadResult(
                DefaultOff(),
                D045PermissionStoreDisposition.InvalidDefaultOff);
        }
    }

    private void Save(D045PermissionControlSnapshot snapshot)
    {
        DemandSnapshot(snapshot);
        Directory.CreateDirectory(_rootDirectory);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
        if (bytes.Length <= 0 || bytes.Length > MaximumStoreBytes)
            throw new InvalidDataException("Permission store exceeds its fixed limit.");

        var temporary = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _storePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
                // Temporary files are never authoritative or read back.
            }
        }
    }

    private static void DemandSnapshot(D045PermissionControlSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != D045PermissionControlSnapshot.CurrentSchemaVersion)
            throw new InvalidDataException("Unknown permission schema version.");
        if (snapshot.Revision <= 0)
            throw new InvalidDataException("Permission revision is invalid.");
        if (snapshot.Enabled is null
            || snapshot.Enabled.Length != D045PermissionControlSnapshot.ExactCapabilityCount)
        {
            throw new InvalidDataException("Permission capability vector is invalid.");
        }
        if (snapshot.UpdatedUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Permission timestamp must be UTC.");
    }

    private static bool IsSchemaThreeLegacyShape(
        D045PermissionControlSnapshot snapshot)
        => snapshot.SchemaVersion
                == D045PermissionControlSnapshot.SchemaThreeVersion
            && snapshot.Enabled?.Length
                == D045PermissionControlSnapshot.SchemaThreeCapabilityCount;

    private static bool IsSchemaFourLegacyShape(
        D045PermissionControlSnapshot snapshot)
        => snapshot.SchemaVersion
                == D045PermissionControlSnapshot.SchemaFourVersion
            && snapshot.Enabled?.Length
                == D045PermissionControlSnapshot.SchemaFourCapabilityCount;

    private static bool IsSchemaTwoLegacyShape(
        D045PermissionControlSnapshot snapshot)
        => snapshot.SchemaVersion
                == D045PermissionControlSnapshot.SchemaTwoVersion
            && snapshot.Enabled?.Length
                == D045PermissionControlSnapshot.SchemaTwoCapabilityCount;

    private static void DemandLegacySnapshot(
        D045PermissionControlSnapshot snapshot,
        int expectedSchemaVersion,
        int expectedCapabilityCount)
    {
        if (snapshot.SchemaVersion != expectedSchemaVersion)
            throw new InvalidDataException("Legacy permission schema is invalid.");
        if (snapshot.Revision <= 0)
            throw new InvalidDataException("Permission revision is invalid.");
        if (snapshot.Enabled is null
            || snapshot.Enabled.Length != expectedCapabilityCount)
        {
            throw new InvalidDataException(
                "Legacy permission capability vector is invalid.");
        }
        if (snapshot.UpdatedUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Permission timestamp must be UTC.");
    }

    private static D045PermissionControlSnapshot MigrateLegacySnapshot(
        D045PermissionControlSnapshot snapshot)
    {
        var migrated = DefaultOff();
        migrated.Revision = snapshot.Revision;
        Array.Copy(snapshot.Enabled, migrated.Enabled, snapshot.Enabled.Length);
        migrated.UpdatedUtc = snapshot.UpdatedUtc;
        return migrated;
    }

    private static bool IsStoreException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException;

    private static long NextRevision(long revision)
        => revision >= long.MaxValue ? 1 : revision + 1;

    private static D045PermissionControlSnapshot DefaultOff()
        => new()
        {
            Revision = 1,
            Enabled = new bool[D045PermissionControlSnapshot.ExactCapabilityCount],
            UpdatedUtc = DateTimeOffset.UnixEpoch
        };

    private sealed class D045LegacyPermissionSnapshot
    {
        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public bool ScreenshotCaptureEnabled { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
