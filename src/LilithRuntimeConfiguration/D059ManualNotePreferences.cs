using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LilithRuntime.Configuration;

public enum D059ManualNoteLoadDisposition { Missing, Ready, Invalid }
public enum D059ManualNoteApplyAdmission { Applied, RevisionConflict, StoreUnavailable }
public sealed record D059ManualNotePreferencesSnapshot(long Revision, bool? Enabled, DateTimeOffset UpdatedUtc);
public sealed record D059ManualNoteLoadResult(D059ManualNotePreferencesSnapshot Snapshot, D059ManualNoteLoadDisposition Disposition)
{
    public bool RequiresExplicitRepair => Disposition == D059ManualNoteLoadDisposition.Invalid;
}
public sealed record D059ManualNoteApplyResult(D059ManualNoteApplyAdmission Admission, D059ManualNotePreferencesSnapshot Snapshot);

// A separate typed game-control preference, not a copy of the BepInEx flag.
// Keeping the original current-v1.json unchanged preserves every schema-5
// reader, including the native Telegram voice selector.
public sealed class D059ManualNotePreferencesStore
{
    public const string StoreFileName = "manual-note-v1.json";
    public const int MaximumStoreBytes = 1024;
    private static readonly D059ManualNotePreferencesSnapshot Missing = new(0, null, DateTimeOffset.UnixEpoch);
    private readonly string _root;
    private readonly string _mutexName;
    public string StorePath { get; }

    public D059ManualNotePreferencesStore(string gameControlDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameControlDirectory)) throw new ArgumentException("Preference directory required.");
        _root = Path.GetFullPath(gameControlDirectory);
        StorePath = Path.Combine(_root, StoreFileName);
        _mutexName = "Local\\LilithAiMod.ManualNotePreferences."
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StorePath.ToUpperInvariant())));
    }

    public static D059ManualNotePreferencesStore ForCurrentUser()
        => new(Path.GetDirectoryName(D046GameControlPreferencesStore.ForCurrentUser().StorePath)!);

    public D059ManualNoteLoadResult Load()
    {
        try
        {
            CheckPlainPath(StorePath);
            using var stream = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is <= 0 or > MaximumStoreBytes) throw new InvalidDataException();
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) throw new InvalidDataException();
            if (!names.SetEquals(new[] { "schemaVersion", "revision", "enabled", "updatedUtc" })
                || !root.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 1
                || !root.GetProperty("revision").TryGetInt64(out var revision) || revision < 1
                || root.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || root.GetProperty("updatedUtc").ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParseExact(root.GetProperty("updatedUtc").GetString(), "O",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)
                || updated.Offset != TimeSpan.Zero) throw new InvalidDataException();
            return new(new(revision, root.GetProperty("enabled").GetBoolean(), updated), D059ManualNoteLoadDisposition.Ready);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { return new(Missing, D059ManualNoteLoadDisposition.Missing); }
        catch (Exception exception) when (IsStoreFailure(exception))
        { return new(Missing, D059ManualNoteLoadDisposition.Invalid); }
    }

    public D059ManualNoteApplyResult TryApply(long expectedRevision, bool enabled, DateTimeOffset now)
    {
        try
        {
            using var mutex = new Mutex(false, _mutexName);
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return new(D059ManualNoteApplyAdmission.StoreUnavailable, Missing);
                var current = Load();
                if (current.RequiresExplicitRepair)
                    return new(D059ManualNoteApplyAdmission.StoreUnavailable, current.Snapshot);
                if (current.Snapshot.Revision != expectedRevision)
                    return new(D059ManualNoteApplyAdmission.RevisionConflict, current.Snapshot);
                var next = new D059ManualNotePreferencesSnapshot(checked(expectedRevision + 1), enabled, now.ToUniversalTime());
                SaveAtomic(next);
                return new(D059ManualNoteApplyAdmission.Applied, next);
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        { return new(D059ManualNoteApplyAdmission.StoreUnavailable, Load().Snapshot); }
    }

    private void SaveAtomic(D059ManualNotePreferencesSnapshot value)
    {
        CheckPlainPath(StorePath);
        Directory.CreateDirectory(_root);
        CheckPlainPath(StorePath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        { schemaVersion = 1, revision = value.Revision, enabled = value.Enabled!.Value, updatedUtc = value.UpdatedUtc.ToString("O") });
        var temporary = Path.Combine(_root, StoreFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                MaximumStoreBytes, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, StorePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CheckPlainPath(string path)
    {
        for (string? cursor = path; cursor != null; cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse preference paths are not accepted.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool IsStoreFailure(Exception exception)
        => exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException or OverflowException or System.Security.SecurityException;
}

// Process-local projection only. Invalid/deleted/regressed preferences cannot
// silently re-enable a legacy flag. No IO, clock or authority store lives here.
public sealed class D059ManualNotePreferenceProjection
{
    private sealed record State(long Revision, bool? Selection, bool Invalid);
    private State _state = new(0, null, false);
    public bool Effective(bool legacyEnabled)
    {
        var state = Volatile.Read(ref _state);
        return !state.Invalid && (state.Selection ?? legacyEnabled);
    }

    public void Observe(D059ManualNoteLoadResult loaded)
    {
        var previous = Volatile.Read(ref _state);
        if (loaded.RequiresExplicitRepair || loaded.Snapshot.Revision < previous.Revision
            || (loaded.Snapshot.Revision == previous.Revision && loaded.Snapshot.Enabled != previous.Selection))
        { Volatile.Write(ref _state, previous with { Invalid = true }); return; }
        Volatile.Write(ref _state, new(loaded.Snapshot.Revision, loaded.Snapshot.Enabled, false));
    }
}
