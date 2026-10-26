using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LilithTextInjector;

internal sealed record FlacMusicCacheRecoveryResult(
    int ExaminedDirectories, int RemovedDirectories, int RemovedFiles,
    int SkippedDirectories, int FailedDirectories);

// A once-per-start, background-only janitor for this feature's temporary WAVs.
// No recursion or age-based deletion outside the exact owned cache namespace.
internal static class FlacMusicCacheRecovery
{
    internal const string CacheDirectoryName = "LilithMusicFlac-v1";
    internal const int MaximumDirectories = 32;
    internal const int MaximumEntriesPerDirectory = 64;

    internal static string NewDirectoryName(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        return processId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
    }

    internal static FlacMusicCacheRecoveryResult CollectAbandoned(string cacheParent,
        DateTime utcNow, Func<int, bool> processIsAlive)
    {
        int examined = 0, removedDirectories = 0, removedFiles = 0, skipped = 0, failed = 0;
        if (utcNow.Kind != DateTimeKind.Utc || utcNow < DateTime.UnixEpoch || processIsAlive == null)
            return new(0, 0, 0, 0, 0);
        try
        {
            if (!TryValidateParent(cacheParent, out string parent)) return new(0, 0, 0, 0, 0);
            DateTime cutoff = utcNow.AddDays(-1);
            // Count all directory names (including unrelated names) against the
            // budget, rather than walking an unbounded tree to find candidates.
            using var directories = Directory.EnumerateDirectories(parent).GetEnumerator();
            while (examined < MaximumDirectories && directories.MoveNext())
            {
                examined++;
                string directory = directories.Current;
                try
                {
                    if (!TryOwnedProcessDirectory(parent, directory, out int processId)
                        || !IsOldOrdinaryDirectory(directory, cutoff)
                        || processId == Environment.ProcessId
                        || !DefinitelyDead(processId, processIsAlive))
                    {
                        skipped++;
                        continue;
                    }
                    List<string>? files = ReadOwnedOldFiles(directory, cutoff);
                    if (files == null || !TryValidateParent(parent, out _)
                        || !IsOldOrdinaryDirectory(directory, cutoff)
                        || !DefinitelyDead(processId, processIsAlive))
                    {
                        skipped++;
                        continue;
                    }
                    // Revalidate immediately before every exact mutation. A
                    // racing unknown entry is never removed: final removal is
                    // non-recursive and will fail unless the directory is empty.
                    foreach (string file in files)
                    {
                        if (!TryValidateParent(parent, out _)
                            || !IsOrdinaryDirectory(directory)
                            || !IsOwnedOldFile(directory, file, cutoff))
                            throw new IOException("flac.cache_changed_during_recovery");
                        File.Delete(file);
                        removedFiles++;
                    }
                    if (!TryValidateParent(parent, out _) || !IsOrdinaryDirectory(directory))
                        throw new IOException("flac.cache_changed_during_recovery");
                    Directory.Delete(directory, false);
                    removedDirectories++;
                }
                catch (Exception error) when (IsExpectedFileError(error)) { failed++; }
            }
        }
        catch (Exception error) when (IsExpectedFileError(error)) { failed++; }
        return new(examined, removedDirectories, removedFiles, skipped, failed);
    }

    private static bool TryValidateParent(string path, out string parent)
    {
        parent = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!string.Equals(Path.GetFileName(full), CacheDirectoryName, StringComparison.Ordinal)) return false;
        // A drive-qualified Windows path cannot be UNC, device syntax, or an
        // alternate data stream. Also exclude mapped network drives.
        if (OperatingSystem.IsWindows())
        {
            if (full.Length < 3 || full[1] != ':' || full.IndexOf(':', 2) >= 0
                || !((full[0] >= 'A' && full[0] <= 'Z') || (full[0] >= 'a' && full[0] <= 'z')))
                return false;
            var drive = new DriveInfo(Path.GetPathRoot(full)!);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram)) return false;
        }
        string? ancestor = full;
        while (ancestor != null)
        {
            if (!IsOrdinaryDirectory(ancestor)) return false;
            ancestor = Path.GetDirectoryName(ancestor);
        }
        parent = full;
        return true;
    }

    private static bool TryOwnedProcessDirectory(string parent, string path, out int processId)
    {
        processId = 0;
        if (!IsImmediateChild(parent, path)) return false;
        string name = Path.GetFileName(path);
        int separator = name.IndexOf('-');
        if (separator <= 0 || !int.TryParse(name.AsSpan(0, separator), NumberStyles.None,
                CultureInfo.InvariantCulture, out processId) || processId <= 0
            || name[..separator] != processId.ToString(CultureInfo.InvariantCulture)) return false;
        return IsCanonicalGuid(name[(separator + 1)..]);
    }

    private static List<string>? ReadOwnedOldFiles(string directory, DateTime cutoff)
    {
        var files = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (files.Count == MaximumEntriesPerDirectory || !IsOwnedOldFile(directory, entry, cutoff))
                return null;
            files.Add(entry);
        }
        return files;
    }

    private static bool IsOwnedOldFile(string directory, string path)
        => IsImmediateChild(directory, path)
        && string.Equals(Path.GetExtension(path), ".wav", StringComparison.Ordinal)
        && IsCanonicalGuid(Path.GetFileNameWithoutExtension(path));

    private static bool IsOwnedOldFile(string directory, string path, DateTime cutoff)
    {
        if (!IsOwnedOldFile(directory, path)) return false;
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
            && File.GetLastWriteTimeUtc(path) < cutoff;
    }

    private static bool IsCanonicalGuid(string text) => text.Length == 32
        && Guid.TryParseExact(text, "N", out Guid value) && value != Guid.Empty
        && text == value.ToString("N");

    private static bool IsImmediateChild(string parent, string path)
        => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), parent,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsOrdinaryDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsOldOrdinaryDirectory(string path, DateTime cutoff)
        => IsOrdinaryDirectory(path) && Directory.GetLastWriteTimeUtc(path) < cutoff;

    private static bool DefinitelyDead(int processId, Func<int, bool> processIsAlive)
    {
        try { return !processIsAlive(processId); }
        // Access denied or unavailable process inspection is not death evidence.
        catch (Exception) { return false; }
    }

    private static bool IsExpectedFileError(Exception error)
        => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException;
}
