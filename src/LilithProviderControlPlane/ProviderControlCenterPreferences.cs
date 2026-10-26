using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithProviderControlPlane;

internal enum ProviderControlCenterPreferenceAdmission
{
    Invalid = 0,
    Accepted = 1,
    Corrupt = 2,
    StorageFailure = 3
}

internal sealed class ProviderControlCenterPreferences
{
    internal bool StartupHealthCheckEnabled { get; }
    internal long Generation { get; }
    internal string Fingerprint { get; }

    internal ProviderControlCenterPreferences(
        bool startupHealthCheckEnabled,
        long generation)
    {
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        StartupHealthCheckEnabled = startupHealthCheckEnabled;
        Generation = generation;
        Fingerprint = UnifiedProviderConfiguredInstance.Hash(
            "D033/CONTROL-CENTER-PREFERENCES/1",
            StartupHealthCheckEnabled ? "1" : "0",
            Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

internal sealed class ProviderControlCenterPreferenceResult
{
    internal ProviderControlCenterPreferenceAdmission Admission { get; }
    internal ProviderControlCenterPreferences? Preferences { get; }
    internal bool Accepted =>
        Admission == ProviderControlCenterPreferenceAdmission.Accepted;

    internal ProviderControlCenterPreferenceResult(
        ProviderControlCenterPreferenceAdmission admission,
        ProviderControlCenterPreferences? preferences)
    {
        if (!Enum.IsDefined(typeof(ProviderControlCenterPreferenceAdmission), admission)
            || admission == ProviderControlCenterPreferenceAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == ProviderControlCenterPreferenceAdmission.Accepted)
            != (preferences != null))
        {
            throw new ArgumentException(
                "Only accepted preference results carry preferences.",
                nameof(preferences));
        }
        Admission = admission;
        Preferences = preferences;
    }
}

internal sealed class ProviderControlCenterPreferenceStore
{
    private const int SchemaVersion = 1;
    private const int MaximumPreferenceBytes = 16 * 1024;
    private readonly string _current;
    private readonly string _previous;

    internal ProviderControlCenterPreferenceStore(
        string localApplicationDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
        {
            throw new ArgumentException(
                "Local application data is required.",
                nameof(localApplicationDataDirectory));
        }
        var root = Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            "LilithAiMod",
            "Config",
            "provider-control-v2");
        _current = Path.Combine(root, "control-center-preferences-v1.json");
        _previous = _current + ".previous";
    }

    internal static ProviderControlCenterPreferenceStore ForCurrentUser()
        => new(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData));

    internal ProviderControlCenterPreferenceResult Load()
    {
        if (!File.Exists(_current))
        {
            return Accepted(new ProviderControlCenterPreferences(
                startupHealthCheckEnabled: false,
                generation: 1));
        }
        var current = Read(_current);
        if (current.Accepted)
            return current;
        var previous = Read(_previous);
        if (previous.Accepted
            && previous.Preferences != null
            && !previous.Preferences.StartupHealthCheckEnabled)
        {
            return previous;
        }

        // Startup networking is opt-in.  A corrupt or ambiguous current file
        // must never resurrect an older enabled generation.  Returning a new
        // in-memory Off generation also keeps the next explicit save usable
        // without treating the damaged file as authority.
        var safeGeneration = previous.Preferences?.Generation ?? 1;
        if (safeGeneration == long.MaxValue)
            safeGeneration = 1;
        else
            safeGeneration++;
        return Accepted(new ProviderControlCenterPreferences(
            startupHealthCheckEnabled: false,
            generation: safeGeneration));
    }

    internal ProviderControlCenterPreferenceResult SaveStartupHealthCheck(
        bool enabled,
        DateTimeOffset now)
    {
        if (now == default)
            throw new ArgumentOutOfRangeException(nameof(now));
        var loaded = Load();
        if (!loaded.Accepted || loaded.Preferences == null)
            return loaded;
        var updated = new ProviderControlCenterPreferences(
            enabled,
            loaded.Preferences.Generation + 1);
        try
        {
            var directory = Path.GetDirectoryName(_current)
                ?? throw new InvalidOperationException("Preference directory is missing.");
            Directory.CreateDirectory(directory);
            var payload = new PreferenceDto
            {
                Schema = SchemaVersion,
                StartupHealthCheckEnabled = updated.StartupHealthCheckEnabled,
                Generation = updated.Generation,
                Fingerprint = updated.Fingerprint,
                UpdatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds()
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            try
            {
                if (bytes.Length == 0 || bytes.Length > MaximumPreferenceBytes)
                    return Rejected(ProviderControlCenterPreferenceAdmission.StorageFailure);
                AtomicWrite(_current, bytes, _previous);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
            return Read(_current);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return Rejected(ProviderControlCenterPreferenceAdmission.StorageFailure);
        }
    }

    private ProviderControlCenterPreferenceResult Read(string path)
    {
        byte[]? bytes = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumPreferenceBytes)
                return Rejected(ProviderControlCenterPreferenceAdmission.Corrupt);
            bytes = File.ReadAllBytes(path);
            var payload = JsonSerializer.Deserialize<PreferenceDto>(bytes);
            if (payload == null
                || payload.Schema != SchemaVersion
                || payload.Generation <= 0
                || !UnifiedProviderCatalogCommit.IsSha256(payload.Fingerprint))
            {
                return Rejected(ProviderControlCenterPreferenceAdmission.Corrupt);
            }
            var preferences = new ProviderControlCenterPreferences(
                payload.StartupHealthCheckEnabled,
                payload.Generation);
            return string.Equals(
                    preferences.Fingerprint,
                    payload.Fingerprint,
                    StringComparison.Ordinal)
                ? Accepted(preferences)
                : Rejected(ProviderControlCenterPreferenceAdmission.Corrupt);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or ArgumentOutOfRangeException)
        {
            return Rejected(ProviderControlCenterPreferenceAdmission.Corrupt);
        }
        finally
        {
            if (bytes != null)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void AtomicWrite(
        string destination,
        byte[] bytes,
        string previous)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
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
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, previous, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static ProviderControlCenterPreferenceResult Accepted(
        ProviderControlCenterPreferences preferences)
        => new(ProviderControlCenterPreferenceAdmission.Accepted, preferences);

    private static ProviderControlCenterPreferenceResult Rejected(
        ProviderControlCenterPreferenceAdmission admission)
        => new(admission, null);

    private sealed class PreferenceDto
    {
        public int Schema { get; set; }
        public bool StartupHealthCheckEnabled { get; set; }
        public long Generation { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public long UpdatedAtUnixMilliseconds { get; set; }
    }
}
