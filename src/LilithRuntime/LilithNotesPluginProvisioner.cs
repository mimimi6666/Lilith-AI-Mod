using System.Security.Cryptography;
using System.Text.Json;

namespace LilithRuntime;

internal sealed record LilithNotesPluginProvisioningReceipt(
    string PluginPath,
    string ManifestSha256,
    int FileCount);

internal static class LilithNotesPluginProvisioner
{
    internal const string PluginId = "lilith_notes";
    internal const string ManifestFileName = "manifest.v1.json";

    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumPluginFileBytes = 1024 * 1024;
    private const int MaximumPluginBytes = 4 * 1024 * 1024;
    private const int MaximumPluginFiles = 32;
    private const int MaximumPluginDirectories = 64;

    internal static async Task<LilithNotesPluginProvisioningReceipt>
        ProvisionAsync(
            LilithRuntimeOptions options,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var sourceRoot = RequireAbsoluteDirectoryPath(
            options.NotesPluginRootPath,
            nameof(options.NotesPluginRootPath));
        EnsureNoReparsePoint(sourceRoot, sourceRoot);
        var manifestBytes = await ReadBoundedFileAsync(
            Path.Combine(sourceRoot, ManifestFileName),
            MaximumManifestBytes,
            "The Lilith notes plugin manifest was missing or invalid.",
            cancellationToken).ConfigureAwait(false);
        var manifestSha256 = Sha256(manifestBytes);
        if (!DigestEquals(manifestSha256, options.NotesPluginManifestSha256))
        {
            throw new LilithRuntimeException(
                "The Lilith notes plugin manifest did not match the Runtime pin.");
        }

        var manifest = DecodeManifest(manifestBytes);
        var trustedPreviousManifestSha256 = options
            .KnownPreviousNotesPluginManifestSha256
            .Select(static value => value.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var pluginParent = Path.Combine(options.HermesHomePath, "plugins");
        var pluginPath = Path.Combine(pluginParent, PluginId);
        Directory.CreateDirectory(pluginParent);
        EnsureNoReparsePoint(pluginParent, options.HermesHomePath);

        if (Directory.Exists(pluginPath))
        {
            var installedManifestBytes = await ReadBoundedFileAsync(
                Path.Combine(pluginPath, ManifestFileName),
                MaximumManifestBytes,
                "The installed Lilith notes manifest was invalid; startup refused without overwriting it.",
                cancellationToken).ConfigureAwait(false);
            var installedManifestSha256 = Sha256(installedManifestBytes);
            var installedIsCurrent = DigestEquals(
                installedManifestSha256,
                manifestSha256);
            if (!installedIsCurrent
                && !trustedPreviousManifestSha256.Contains(
                    installedManifestSha256))
            {
                throw new LilithRuntimeException(
                    "The installed Lilith notes manifest was not a trusted release; startup refused without overwriting it.");
            }

            var installedManifest = DecodeManifest(installedManifestBytes);
            await VerifyInstalledAsync(
                pluginPath,
                installedManifest,
                installedManifestBytes,
                cancellationToken).ConfigureAwait(false);
            if (installedIsCurrent)
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        manifestBytes,
                        installedManifestBytes))
                {
                    throw new LilithRuntimeException(
                        "The installed Lilith notes manifest differed from the pinned release.");
                }
            }
            else
            {
                await UpgradeCleanAsync(
                    sourceRoot,
                    pluginPath,
                    manifest,
                    manifestBytes,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await InstallNewAsync(
                sourceRoot,
                pluginPath,
                manifest,
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
        }

        return new LilithNotesPluginProvisioningReceipt(
            pluginPath,
            manifestSha256,
            manifest.Files.Count);
    }

    private static async Task InstallNewAsync(
        string sourceRoot,
        string pluginPath,
        PluginManifest manifest,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        var pluginParent = Path.GetDirectoryName(pluginPath)
            ?? throw new LilithRuntimeException(
                "The Lilith notes plugin destination was invalid.");
        var stagingPath = Path.Combine(
            pluginParent,
            ".lilith_notes-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingPath);
            var totalBytes = 0L;
            foreach (var entry in manifest.Files)
            {
                var sourcePath = ResolveContainedPath(sourceRoot, entry.Path);
                EnsureNoReparsePoint(
                    Path.GetDirectoryName(sourcePath)
                    ?? throw new LilithRuntimeException(
                        "A Lilith notes plugin asset path was invalid."),
                    sourceRoot);
                var bytes = await ReadBoundedFileAsync(
                    sourcePath,
                    MaximumPluginFileBytes,
                    "A Lilith notes plugin asset was missing or invalid.",
                    cancellationToken).ConfigureAwait(false);
                totalBytes = checked(totalBytes + bytes.Length);
                if (totalBytes > MaximumPluginBytes)
                {
                    throw new LilithRuntimeException(
                        "The Lilith notes plugin payload exceeded its bound.");
                }
                if (!string.Equals(
                        Sha256(bytes),
                        entry.Sha256,
                        StringComparison.Ordinal))
                {
                    throw new LilithRuntimeException(
                        "A Lilith notes plugin asset did not match its manifest.");
                }

                var destinationPath = ResolveContainedPath(stagingPath, entry.Path);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationPath)
                    ?? throw new LilithRuntimeException(
                        "A Lilith notes plugin path was invalid."));
                await WriteNewAsync(destinationPath, bytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            await WriteNewAsync(
                Path.Combine(stagingPath, ManifestFileName),
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
            await VerifyInstalledAsync(
                stagingPath,
                manifest,
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
            Directory.Move(stagingPath, pluginPath);
        }
        catch (IOException) when (Directory.Exists(pluginPath))
        {
            await VerifyInstalledAsync(
                pluginPath,
                manifest,
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
        }

        await VerifyInstalledAsync(
            pluginPath,
            manifest,
            manifestBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpgradeCleanAsync(
        string sourceRoot,
        string pluginPath,
        PluginManifest manifest,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        var pluginParent = Path.GetDirectoryName(pluginPath)
            ?? throw new LilithRuntimeException(
                "The Lilith notes plugin destination was invalid.");
        var nonce = Guid.NewGuid().ToString("N");
        var candidatePath = Path.Combine(
            pluginParent,
            ".lilith_notes-candidate-" + nonce);
        var previousPath = Path.Combine(
            pluginParent,
            ".lilith_notes-previous-" + nonce);
        var previousMoved = false;
        var candidateActivated = false;
        try
        {
            await InstallNewAsync(
                sourceRoot,
                candidatePath,
                manifest,
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
            Directory.Move(pluginPath, previousPath);
            previousMoved = true;
            try
            {
                Directory.Move(candidatePath, pluginPath);
                candidateActivated = true;
            }
            catch
            {
                Directory.Move(previousPath, pluginPath);
                previousMoved = false;
                throw;
            }

            try
            {
                await VerifyInstalledAsync(
                    pluginPath,
                    manifest,
                    manifestBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Directory.Move(pluginPath, candidatePath);
                candidateActivated = false;
                Directory.Move(previousPath, pluginPath);
                previousMoved = false;
                throw;
            }

            try
            {
                Directory.Delete(previousPath, recursive: true);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
            {
                // A verified active candidate is safer than reversing a
                // completed cutover because an inert rollback directory is busy.
            }
            previousMoved = false;
        }
        finally
        {
            if (!candidateActivated && Directory.Exists(candidatePath))
                Directory.Delete(candidatePath, recursive: true);
            if (previousMoved && !Directory.Exists(pluginPath))
                Directory.Move(previousPath, pluginPath);
        }
    }

    private static async Task VerifyInstalledAsync(
        string pluginPath,
        PluginManifest manifest,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        EnsureNoReparsePoint(
            pluginPath,
            Path.GetDirectoryName(pluginPath)
            ?? throw new LilithRuntimeException(
                "The Lilith notes plugin destination was invalid."));
        var expectedFiles = new HashSet<string>(
            manifest.Files.Select(static entry => entry.Path)
                .Append(ManifestFileName),
            StringComparer.Ordinal);
        var expectedDirectories = manifest.Files
            .SelectMany(static entry => ParentDirectories(entry.Path))
            .ToHashSet(StringComparer.Ordinal);
        var bytecodeCache = new LilithPluginBytecodeCache(
            pluginPath, manifest.Files.Select(static entry => entry.Path));
        if (!EnumerateDirectoriesSafe(pluginPath)
                .Where(path => !bytecodeCache.IsCacheDirectory(path))
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedDirectories))
        {
            throw new LilithRuntimeException(
                "The installed Lilith notes plugin contained unexpected directories; startup refused.");
        }

        var actualFiles = Directory.EnumerateFiles(
                pluginPath,
                "*",
                SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(
                Path.GetRelativePath(pluginPath, path)))
            .Where(path => !bytecodeCache.IsCacheFile(path))
            .ToHashSet(StringComparer.Ordinal);
        if (!actualFiles.SetEquals(expectedFiles))
        {
            throw new LilithRuntimeException(
                "The installed Lilith notes plugin contained unexpected or missing files; startup refused.");
        }

        foreach (var entry in manifest.Files)
        {
            var bytes = await ReadBoundedFileAsync(
                ResolveContainedPath(pluginPath, entry.Path),
                MaximumPluginFileBytes,
                "An installed Lilith notes plugin file was invalid.",
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    Sha256(bytes),
                    entry.Sha256,
                    StringComparison.Ordinal))
            {
                throw new LilithRuntimeException(
                    "The installed Lilith notes plugin drifted; startup refused without overwriting it.");
            }
        }

        var installedManifest = await ReadBoundedFileAsync(
            Path.Combine(pluginPath, ManifestFileName),
            MaximumManifestBytes,
            "The installed Lilith notes manifest was invalid.",
            cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                manifestBytes,
                installedManifest))
        {
            throw new LilithRuntimeException(
                "The installed Lilith notes manifest drifted; startup refused.");
        }
        bytecodeCache.RemoveAfterSourceVerification();
    }

    private static PluginManifest DecodeManifest(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 6
            });
            var root = document.RootElement;
            RequireExactProperties(
                root,
                "schema_version",
                "plugin_id",
                "plugin_version",
                "files");
            if (root.GetProperty("schema_version").GetInt32() != 1
                || !string.Equals(
                    root.GetProperty("plugin_id").GetString(),
                    PluginId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }

            var version = RequiredBoundedString(
                root.GetProperty("plugin_version"),
                32);
            var filesValue = root.GetProperty("files");
            if (filesValue.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException();

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<PluginFile>();
            foreach (var item in filesValue.EnumerateArray())
            {
                RequireExactProperties(item, "path", "sha256");
                var path = NormalizeRelativePath(
                    RequiredBoundedString(item.GetProperty("path"), 160));
                if (string.Equals(path, ManifestFileName, StringComparison.Ordinal)
                    || !paths.Add(path))
                {
                    throw new InvalidDataException();
                }
                var sha256 = RequiredBoundedString(
                    item.GetProperty("sha256"),
                    64).ToUpperInvariant();
                if (!IsSha256(sha256))
                    throw new InvalidDataException();
                files.Add(new PluginFile(path, sha256));
            }
            if (files.Count is < 2 or > MaximumPluginFiles)
                throw new InvalidDataException();
            return new PluginManifest(version, files.AsReadOnly());
        }
        catch (Exception exception) when (exception is JsonException
                                          or InvalidDataException
                                          or InvalidOperationException
                                          or FormatException
                                          or OverflowException)
        {
            throw new LilithRuntimeException(
                "The Lilith notes plugin manifest was rejected.");
        }
    }

    private static void RequireExactProperties(
        JsonElement value,
        params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.EnumerateObject().Select(static property => property.Name)
                .SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException();
        }
    }

    private static string RequiredBoundedString(
        JsonElement value,
        int maximumLength)
    {
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || string.IsNullOrWhiteSpace(text)
            || text.Length > maximumLength
            || text.Any(char.IsControl))
        {
            throw new InvalidDataException();
        }
        return text;
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.EndsWith("/", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(normalized)
            || normalized.Split('/').Any(static segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException();
        }
        return normalized;
    }

    private static string ResolveContainedPath(string root, string relative)
    {
        var normalized = NormalizeRelativePath(relative);
        var canonicalRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(
            canonicalRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = canonicalRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison))
        {
            throw new LilithRuntimeException(
                "A Lilith notes plugin path escaped its root.");
        }
        return path;
    }

    private static IEnumerable<string> ParentDirectories(string relative)
    {
        var segments = NormalizeRelativePath(relative).Split('/');
        for (var count = 1; count < segments.Length; count++)
            yield return string.Join('/', segments.Take(count));
    }

    private static HashSet<string> EnumerateDirectoriesSafe(string root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            foreach (var child in parent.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new LilithRuntimeException(
                        "The installed Lilith notes plugin crossed a reparse point.");
                }
                var relative = NormalizeRelativePath(
                    Path.GetRelativePath(root, child.FullName));
                if (!result.Add(relative)
                    || result.Count > MaximumPluginDirectories)
                {
                    throw new LilithRuntimeException(
                        "The installed Lilith notes plugin directory closure was invalid.");
                }
                pending.Push(child);
            }
        }
        return result;
    }

    private static string RequireAbsoluteDirectoryPath(
        string value,
        string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                "A non-empty absolute directory path is required.",
                parameter);
        }
        return Path.GetFullPath(value);
    }

    private static void EnsureNoReparsePoint(string path, string root)
    {
        var canonicalRoot = Path.GetFullPath(root);
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new LilithRuntimeException(
                    "The Lilith notes installation path crossed a reparse point.");
            }
            if (string.Equals(current.FullName, canonicalRoot, PathComparison))
                return;
            current = current.Parent;
        }
        throw new LilithRuntimeException(
            "The Lilith notes installation path escaped its profile.");
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        string error,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length <= 0
                || info.Length > maximumBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new LilithRuntimeException(error);
            }
            return await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LilithRuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(error);
        }
    }

    private static async Task WriteNewAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static bool IsSha256(string value)
        => value.Length == 64
           && value.All(static character => character is >= '0' and <= '9'
               or >= 'A' and <= 'F');

    private static bool DigestEquals(string left, string right)
    {
        if (!IsSha256(left.ToUpperInvariant())
            || !IsSha256(right.ToUpperInvariant()))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed record PluginManifest(
        string Version,
        IReadOnlyList<PluginFile> Files);

    private sealed record PluginFile(string Path, string Sha256);
}
