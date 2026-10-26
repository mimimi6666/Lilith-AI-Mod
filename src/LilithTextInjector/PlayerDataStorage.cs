using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithTextInjector;

public enum PlayerDataMigrationState
{
    CreatedFresh,
    Migrated,
    AlreadyCurrent,
    ExistingDestinationPreserved,
    LegacyFallback
}

public sealed class PlayerDataStorageResult
{
    public string RootDirectory { get; }
    public string ProfileDirectory { get; }
    public string MemoryPath { get; }
    public string MemoryCoreDirectory { get; }
    public string ConversationHistoryDirectory { get; }
    public string ConversationHistoryMigrationMarkerPath { get; }
    public string AiNoteStatePath { get; }
    public string StateDirectory { get; }
    public string CommunicationSignalStatePath { get; }
    public string PlayerAuthoredIdentityStatePath { get; }
    public string ApplicationLauncherPath { get; }
    public PlayerDataMigrationState State { get; }
    public bool UsesLegacyFallback => State == PlayerDataMigrationState.LegacyFallback;
    public int MigratedFileCount { get; }
    public long MigratedBytes { get; }

    internal PlayerDataStorageResult(
        string rootDirectory,
        string profileDirectory,
        string memoryPath,
        string memoryCoreDirectory,
        string conversationHistoryDirectory,
        string conversationHistoryMigrationMarkerPath,
        string aiNoteStatePath,
        string stateDirectory,
        string communicationSignalStatePath,
        string playerAuthoredIdentityStatePath,
        string applicationLauncherPath,
        PlayerDataMigrationState state,
        int migratedFileCount,
        long migratedBytes)
    {
        RootDirectory = rootDirectory;
        ProfileDirectory = profileDirectory;
        MemoryPath = memoryPath;
        MemoryCoreDirectory = memoryCoreDirectory;
        ConversationHistoryDirectory = conversationHistoryDirectory;
        ConversationHistoryMigrationMarkerPath = conversationHistoryMigrationMarkerPath;
        AiNoteStatePath = aiNoteStatePath;
        StateDirectory = stateDirectory;
        CommunicationSignalStatePath = communicationSignalStatePath;
        PlayerAuthoredIdentityStatePath = playerAuthoredIdentityStatePath;
        ApplicationLauncherPath = applicationLauncherPath;
        State = state;
        MigratedFileCount = migratedFileCount;
        MigratedBytes = migratedBytes;
    }
}

public static class PlayerDataStorage
{
    public const string ProductDirectoryName = "LilithAiMod";
    public const string ProfileName = "local";
    public const string MigrationMarkerFileName = ".migration-v1.complete.json";

    private const string MemoryFileName = "memory.json";
    private const string MemoryCoreDirectoryName = "memory-core-shadow-v1";
    private const string ConversationHistoryDirectoryName = "conversation-history-v1";
    private const string AiNoteStateFileName = "ai-note-state.json";
    private const string CommunicationSignalStateFileName = "communication-signals-v1.json";
    private const string PlayerAuthoredIdentityStateFileName = "player-authored-identity-v1.json";
    private const string ApplicationLauncherFileName = "applications.json";

    public static string BuildRootDirectory(string localApplicationDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
            throw new ArgumentException("The LocalApplicationData directory is unavailable.", nameof(localApplicationDataDirectory));
        return Path.GetFullPath(Path.Combine(localApplicationDataDirectory, ProductDirectoryName));
    }

    public static string BuildProfileDirectory(string localApplicationDataDirectory)
        => Path.Combine(BuildRootDirectory(localApplicationDataDirectory), "PlayerData", "profiles", ProfileName);

    public static PlayerDataStorageResult Prepare(
        string legacyDataDirectory,
        string localApplicationDataDirectory,
        Action<string>? log = null,
        Action<string>? warn = null)
    {
        if (string.IsNullOrWhiteSpace(legacyDataDirectory))
            throw new ArgumentException("The legacy data directory is required.", nameof(legacyDataDirectory));

        var legacy = Path.GetFullPath(legacyDataDirectory);
        string root;
        try
        {
            root = BuildRootDirectory(localApplicationDataDirectory);
        }
        catch (Exception exception)
        {
            warn?.Invoke($"Player data root is unavailable; continuing from the protected legacy copy: {exception.GetType().Name}.");
            return BuildLegacyFallback(legacy, legacy, Path.Combine(legacy, ApplicationLauncherFileName));
        }

        ProfilePreparation profile;
        try
        {
            profile = PrepareProfile(legacy, root, log);
        }
        catch (Exception exception)
        {
            warn?.Invoke($"Player data migration did not complete; the original data remains authoritative: {exception.GetType().Name}.");
            return BuildLegacyFallback(root, legacy, PrepareApplicationLauncherPath(legacy, root, log, warn));
        }

        var applicationLauncherPath = PrepareApplicationLauncherPath(legacy, root, log, warn);
        var isLegacy = string.Equals(profile.ActiveDirectory, legacy, StringComparison.OrdinalIgnoreCase);
        var aiNotePath = isLegacy
            ? Path.Combine(profile.ActiveDirectory, AiNoteStateFileName)
            : Path.Combine(profile.ActiveDirectory, "state", AiNoteStateFileName);
        var stateDirectory = isLegacy
            ? profile.ActiveDirectory
            : Path.Combine(profile.ActiveDirectory, "state");
        var communicationSignalStatePath = isLegacy
            ? Path.Combine(profile.ActiveDirectory, CommunicationSignalStateFileName)
            : Path.Combine(profile.ActiveDirectory, "state", CommunicationSignalStateFileName);
        var playerAuthoredIdentityStatePath = isLegacy
            ? Path.Combine(profile.ActiveDirectory, PlayerAuthoredIdentityStateFileName)
            : Path.Combine(profile.ActiveDirectory, "state", PlayerAuthoredIdentityStateFileName);

        return new PlayerDataStorageResult(
            root,
            profile.ActiveDirectory,
            Path.Combine(profile.ActiveDirectory, MemoryFileName),
            Path.Combine(profile.ActiveDirectory, MemoryCoreDirectoryName),
            Path.Combine(profile.ActiveDirectory, ConversationHistoryDirectoryName),
            Path.Combine(profile.ActiveDirectory, ConversationHistoryDirectoryName, "legacy-import-v1.marker.json"),
            aiNotePath,
            stateDirectory,
            communicationSignalStatePath,
            playerAuthoredIdentityStatePath,
            applicationLauncherPath,
            profile.State,
            profile.FilesCopied,
            profile.BytesCopied);
    }

    internal static PlayerDataStorageResult PrepareIsolatedD029(
        D029IsolatedTestProfile profile,
        Action<string>? log = null)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        var destination = Path.GetFullPath(
            profile.PlayerDataDirectory);
        var scope = Path.GetFullPath(profile.ScopeDirectory);
        var requiredPrefix = scope.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The isolated player-data directory escaped its scope.");
        }

        var marker = Path.Combine(
            scope,
            ".d029-isolated-profile-v1");
        var profileAlreadyExisted = File.Exists(marker);
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(
            Path.Combine(destination, "state"));
        Directory.CreateDirectory(profile.NativeNotesDirectory);
        Directory.CreateDirectory(
            Path.GetDirectoryName(profile.ApplicationLauncherPath)
                ?? scope);

        if (!profileAlreadyExisted)
        {
            var temporary = marker
                + "."
                + Guid.NewGuid().ToString("N")
                + ".tmp";
            File.WriteAllText(
                temporary,
                D029IsolatedTestProfile.Schema
                    + Environment.NewLine
                    + profile.ProfileId
                    + Environment.NewLine,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, marker);
        }
        else
        {
            var lines = File.ReadAllLines(marker);
            if (lines.Length < 2
                || !string.Equals(
                    lines[0],
                    D029IsolatedTestProfile.Schema,
                    StringComparison.Ordinal)
                || !string.Equals(
                    lines[1],
                    profile.ProfileId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The isolated D-029 Profile marker does not match "
                    + "the requested identity.");
            }
        }

        var stateDirectory = Path.Combine(destination, "state");
        var communicationSignalStatePath = Path.Combine(
            destination,
            "state",
            CommunicationSignalStateFileName);
        var playerAuthoredIdentityStatePath = Path.Combine(
            destination,
            "state",
            PlayerAuthoredIdentityStateFileName);
        var syntheticProjectionSourcesCreated =
            PrepareIsolatedD029ProjectionSources(
                communicationSignalStatePath,
                playerAuthoredIdentityStatePath,
                DateTimeOffset.UtcNow);

        log?.Invoke(
            "D-029 isolated Profile ready: id="
            + profile.ProfileId
            + "; syntheticProjectionSourcesCreated="
            + syntheticProjectionSourcesCreated
            + "; production player data was not copied or opened.");
        return new PlayerDataStorageResult(
            scope,
            destination,
            Path.Combine(destination, MemoryFileName),
            Path.Combine(destination, MemoryCoreDirectoryName),
            Path.Combine(
                destination,
                ConversationHistoryDirectoryName),
            Path.Combine(
                destination,
                ConversationHistoryDirectoryName,
                "legacy-import-v1.marker.json"),
            Path.Combine(
                destination,
                "state",
                AiNoteStateFileName),
            stateDirectory,
            communicationSignalStatePath,
            playerAuthoredIdentityStatePath,
            profile.ApplicationLauncherPath,
            profileAlreadyExisted
                ? PlayerDataMigrationState.AlreadyCurrent
                : PlayerDataMigrationState.CreatedFresh,
            0,
            0);
    }

    private static int PrepareIsolatedD029ProjectionSources(
        string communicationSignalStatePath,
        string playerAuthoredIdentityStatePath,
        DateTimeOffset now)
    {
        var created = 0;
        if (!File.Exists(communicationSignalStatePath)
            && !File.Exists(communicationSignalStatePath + ".previous"))
        {
            new CommunicationPatternStore(communicationSignalStatePath)
                .Save(CommunicationPatternState.CreateDefault(now));
            created++;
        }
        if (!File.Exists(playerAuthoredIdentityStatePath)
            && !File.Exists(playerAuthoredIdentityStatePath + ".previous"))
        {
            new PlayerAuthoredIdentityStore(playerAuthoredIdentityStatePath)
                .Save(PlayerAuthoredIdentityState.CreateDefault(now));
            created++;
        }
        return created;
    }

    private static ProfilePreparation PrepareProfile(string legacy, string root, Action<string>? log)
    {
        var profilesDirectory = Path.Combine(root, "PlayerData", "profiles");
        var destination = Path.Combine(profilesDirectory, ProfileName);
        var marker = Path.Combine(destination, MigrationMarkerFileName);

        if (Directory.Exists(destination))
        {
            if (File.Exists(marker))
            {
                log?.Invoke("Player data storage is already using LocalAppData.");
                return new ProfilePreparation(destination, PlayerDataMigrationState.AlreadyCurrent, 0, 0);
            }

            log?.Invoke("An existing LocalAppData player profile was preserved without overwriting it; legacy data remains untouched.");
            return new ProfilePreparation(destination, PlayerDataMigrationState.ExistingDestinationPreserved, 0, 0);
        }

        Directory.CreateDirectory(profilesDirectory);
        var staging = Path.Combine(profilesDirectory, $".{ProfileName}.migration-v1-{Guid.NewGuid():N}");
        var counter = new CopyCounter();
        try
        {
            Directory.CreateDirectory(staging);
            CopyLegacyFileIfPresent(
                Path.Combine(legacy, MemoryFileName),
                Path.Combine(staging, MemoryFileName),
                counter);
            CopyLegacyDirectoryIfPresent(
                Path.Combine(legacy, MemoryCoreDirectoryName),
                Path.Combine(staging, MemoryCoreDirectoryName),
                counter);
            CopyLegacyDirectoryIfPresent(
                Path.Combine(legacy, ConversationHistoryDirectoryName),
                Path.Combine(staging, ConversationHistoryDirectoryName),
                counter);
            CopyLegacyFileIfPresent(
                Path.Combine(legacy, AiNoteStateFileName),
                Path.Combine(staging, "state", AiNoteStateFileName),
                counter);
            CopyLegacyFileIfPresent(
                Path.Combine(legacy, CommunicationSignalStateFileName),
                Path.Combine(staging, "state", CommunicationSignalStateFileName),
                counter);
            CopyLegacyFileIfPresent(
                Path.Combine(legacy, PlayerAuthoredIdentityStateFileName),
                Path.Combine(staging, "state", PlayerAuthoredIdentityStateFileName),
                counter);

            WriteMigrationReceipt(staging, counter);
            Directory.Move(staging, destination);
            var state = counter.Files == 0
                ? PlayerDataMigrationState.CreatedFresh
                : PlayerDataMigrationState.Migrated;
            log?.Invoke(counter.Files == 0
                ? "Initialized protected LocalAppData player storage."
                : $"Migrated {counter.Files} player-data files to LocalAppData after byte-for-byte verification; the original copy was retained.");
            return new ProfilePreparation(destination, state, counter.Files, counter.Bytes);
        }
        catch
        {
            TryDeleteStagingDirectory(staging);
            throw;
        }
    }

    private static string PrepareApplicationLauncherPath(
        string legacy,
        string root,
        Action<string>? log,
        Action<string>? warn)
    {
        var legacyPath = Path.Combine(legacy, ApplicationLauncherFileName);
        var configDirectory = Path.Combine(root, "Config");
        var destination = Path.Combine(configDirectory, ApplicationLauncherFileName);
        if (File.Exists(destination))
            return destination;

        try
        {
            Directory.CreateDirectory(configDirectory);
            if (!File.Exists(legacyPath))
                return destination;

            var temporary = destination + $".migration-{Guid.NewGuid():N}.tmp";
            try
            {
                CopyFileAndVerify(legacyPath, temporary);
                File.Move(temporary, destination);
                log?.Invoke("Migrated the editable application launcher list to LocalAppData Config; the original copy was retained.");
                return destination;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        catch (Exception exception)
        {
            warn?.Invoke($"Could not migrate the editable application launcher list; continuing from the original copy: {exception.GetType().Name}.");
            return legacyPath;
        }
    }

    private static PlayerDataStorageResult BuildLegacyFallback(
        string root,
        string legacy,
        string applicationLauncherPath)
        => new(
            root,
            legacy,
            Path.Combine(legacy, MemoryFileName),
            Path.Combine(legacy, MemoryCoreDirectoryName),
            Path.Combine(legacy, ConversationHistoryDirectoryName),
            Path.Combine(legacy, ConversationHistoryDirectoryName, "legacy-import-v1.marker.json"),
            Path.Combine(legacy, AiNoteStateFileName),
            legacy,
            Path.Combine(legacy, CommunicationSignalStateFileName),
            Path.Combine(legacy, PlayerAuthoredIdentityStateFileName),
            applicationLauncherPath,
            PlayerDataMigrationState.LegacyFallback,
            0,
            0);

    private static void CopyLegacyFileIfPresent(string source, string destination, CopyCounter counter)
    {
        if (!File.Exists(source))
            return;
        CopyFileAndVerify(source, destination);
        counter.Add(new FileInfo(destination).Length);
    }

    private static void CopyLegacyDirectoryIfPresent(string source, string destination, CopyCounter counter)
    {
        if (!Directory.Exists(source))
            return;
        CopyDirectoryAndVerify(source, destination, counter);
    }

    private static void CopyDirectoryAndVerify(string source, string destination, CopyCounter counter)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            var target = Path.Combine(destination, Path.GetFileName(file));
            CopyFileAndVerify(file, target);
            counter.Add(new FileInfo(target).Length);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            CopyDirectoryAndVerify(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)),
                counter);
        }
    }

    private static void CopyFileAndVerify(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
        File.Copy(source, destination, overwrite: false);

        var sourceInfo = new FileInfo(source);
        var destinationInfo = new FileInfo(destination);
        if (sourceInfo.Length != destinationInfo.Length
            || !CryptographicOperations.FixedTimeEquals(ComputeSha256(source), ComputeSha256(destination)))
        {
            throw new IOException("A copied player-data file failed integrity verification.");
        }
    }

    private static byte[] ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var algorithm = SHA256.Create();
        return algorithm.ComputeHash(stream);
    }

    private static void WriteMigrationReceipt(string staging, CopyCounter counter)
    {
        var marker = Path.Combine(staging, MigrationMarkerFileName);
        var temporary = marker + ".tmp";
        var receipt = new MigrationReceipt
        {
            Schema = "lilith-player-data-migration-v1",
            CompletedAtUtc = DateTimeOffset.UtcNow,
            FilesCopied = counter.Files,
            BytesCopied = counter.Bytes,
            OriginalDataRetained = true
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, marker);
    }

    private static void TryDeleteStagingDirectory(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class CopyCounter
    {
        internal int Files { get; private set; }
        internal long Bytes { get; private set; }

        internal void Add(long bytes)
        {
            Files++;
            Bytes += bytes;
        }
    }

    private sealed class ProfilePreparation
    {
        internal string ActiveDirectory { get; }
        internal PlayerDataMigrationState State { get; }
        internal int FilesCopied { get; }
        internal long BytesCopied { get; }

        internal ProfilePreparation(
            string activeDirectory,
            PlayerDataMigrationState state,
            int filesCopied,
            long bytesCopied)
        {
            ActiveDirectory = activeDirectory;
            State = state;
            FilesCopied = filesCopied;
            BytesCopied = bytesCopied;
        }
    }

    private sealed class MigrationReceipt
    {
        public string Schema { get; set; } = string.Empty;
        public DateTimeOffset CompletedAtUtc { get; set; }
        public int FilesCopied { get; set; }
        public long BytesCopied { get; set; }
        public bool OriginalDataRetained { get; set; }
    }
}
