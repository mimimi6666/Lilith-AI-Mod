using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

internal sealed record LilithRelationshipMemoryProvisioningReceipt(
    string PluginPath,
    string ManifestSha256,
    int FileCount,
    string ManagedConfigPath,
    string ManagedConfigSha256);

internal static class LilithRelationshipMemoryProvisioner
{
    internal const string PluginId = "lilith_relationship";
    internal const string ManifestFileName = "manifest.v1.json";
    internal const string ManagedConfigText =
        "memory:\n"
        + "  provider: lilith_relationship\n"
        + "  user_profile_write_authority: lilith_relationship\n";
    internal const string LegacyManagedConfigText =
        "memory:\n"
        + "  provider: lilith_relationship\n";

    internal static string ManagedConfigSha256 { get; } = Sha256(
        new UTF8Encoding(false).GetBytes(ManagedConfigText));

    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumPluginFileBytes = 1024 * 1024;
    private const int MaximumPluginBytes = 4 * 1024 * 1024;
    private const int MaximumPluginFiles = 32;

    internal static string ManagedConfigDirectory(string hermesHomePath)
        => Path.Combine(
            RequireAbsoluteDirectoryPath(hermesHomePath, nameof(hermesHomePath)),
            "runtime",
            "managed-lilith");

    internal static async Task<LilithRelationshipMemoryProvisioningReceipt>
        ProvisionAsync(
            LilithRuntimeOptions options,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var sourceRoot = RequireAbsoluteDirectoryPath(
            options.RelationshipMemoryPluginRootPath,
            nameof(options.RelationshipMemoryPluginRootPath));
        EnsureNoReparsePoint(sourceRoot, sourceRoot);
        var manifestPath = Path.Combine(sourceRoot, ManifestFileName);
        var manifestBytes = await ReadBoundedFileAsync(
            manifestPath,
            MaximumManifestBytes,
            "The Lilith relationship-memory plugin manifest was missing or invalid.",
            cancellationToken).ConfigureAwait(false);
        var manifestSha256 = Sha256(manifestBytes);
        if (!DigestEquals(
                manifestSha256,
                options.RelationshipMemoryPluginManifestSha256))
        {
            throw new LilithRuntimeException(
                "The Lilith relationship-memory plugin manifest did not match the Runtime pin.");
        }
        var manifest = DecodeManifest(manifestBytes);
        var trustedPreviousManifestSha256 = options
            .KnownPreviousRelationshipMemoryPluginManifestSha256
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
                "The installed relationship-memory manifest was invalid; startup refused without overwriting it.",
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
                    "The installed relationship-memory manifest was not a trusted release; startup refused without overwriting it.");
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
                        "The installed relationship-memory manifest differed from the pinned release.");
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

        var managedDirectory = ManagedConfigDirectory(options.HermesHomePath);
        var managedParent = Path.GetDirectoryName(managedDirectory)
            ?? throw new LilithRuntimeException(
                "The Runtime-managed configuration path was invalid.");
        Directory.CreateDirectory(managedParent);
        EnsureNoReparsePoint(managedParent, options.HermesHomePath);
        Directory.CreateDirectory(managedDirectory);
        EnsureNoReparsePoint(managedDirectory, options.HermesHomePath);
        VerifyManagedDirectoryClosure(
            managedDirectory,
            allowMissingConfig: true);
        var managedConfigPath = Path.Combine(managedDirectory, "config.yaml");
        var managedConfigBytes = new UTF8Encoding(false).GetBytes(
            ManagedConfigText);
        await CreateOrMigrateManagedConfigAsync(
            managedConfigPath,
            managedConfigBytes,
            "The Runtime-owned relationship-memory configuration drifted; startup refused.",
            cancellationToken).ConfigureAwait(false);
        VerifyManagedDirectoryClosure(
            managedDirectory,
            allowMissingConfig: false);

        return new LilithRelationshipMemoryProvisioningReceipt(
            pluginPath,
            manifestSha256,
            manifest.Files.Count,
            managedConfigPath,
            Sha256(managedConfigBytes));
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
                "The relationship-memory plugin destination was invalid.");
        var stagingPath = Path.Combine(
            pluginParent,
            ".lilith_relationship-stage-" + Guid.NewGuid().ToString("N"));
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
                        "A relationship-memory plugin asset path was invalid."),
                    sourceRoot);
                var bytes = await ReadBoundedFileAsync(
                    sourcePath,
                    MaximumPluginFileBytes,
                    "A relationship-memory plugin asset was missing or invalid.",
                    cancellationToken).ConfigureAwait(false);
                totalBytes = checked(totalBytes + bytes.Length);
                if (totalBytes > MaximumPluginBytes)
                {
                    throw new LilithRuntimeException(
                        "The relationship-memory plugin payload exceeded its bound.");
                }
                if (!string.Equals(
                        Sha256(bytes),
                        entry.Sha256,
                        StringComparison.Ordinal))
                {
                    throw new LilithRuntimeException(
                        "A relationship-memory plugin asset did not match its manifest.");
                }

                var destinationPath = ResolveContainedPath(stagingPath, entry.Path);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationPath)
                    ?? throw new LilithRuntimeException(
                        "A relationship-memory plugin path was invalid."));
                await WriteNewAsync(
                    destinationPath,
                    bytes,
                    cancellationToken).ConfigureAwait(false);
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
            {
                Directory.Delete(stagingPath, recursive: true);
            }
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
                "The relationship-memory plugin destination was invalid.");
        var nonce = Guid.NewGuid().ToString("N");
        var candidatePath = Path.Combine(
            pluginParent,
            ".lilith_relationship-candidate-" + nonce);
        var previousPath = Path.Combine(
            pluginParent,
            ".lilith_relationship-previous-" + nonce);
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
                // The verified candidate is already active. A retained hidden
                // previous directory is inert and safer than failing startup
                // after a successful atomic cutover.
            }
            previousMoved = false;
        }
        finally
        {
            if (!candidateActivated && Directory.Exists(candidatePath))
            {
                Directory.Delete(candidatePath, recursive: true);
            }
            if (previousMoved && !Directory.Exists(pluginPath))
            {
                Directory.Move(previousPath, pluginPath);
            }
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
                "The relationship-memory plugin destination was invalid."));
        var expected = new HashSet<string>(
            manifest.Files.Select(entry => entry.Path)
                .Append(ManifestFileName),
            StringComparer.Ordinal);
        var expectedDirectories = manifest.Files
            .SelectMany(static entry => ParentDirectories(entry.Path))
            .ToHashSet(StringComparer.Ordinal);
        var bytecodeCache = new LilithPluginBytecodeCache(
            pluginPath, manifest.Files.Select(static entry => entry.Path));
        var actualDirectories = EnumerateDirectoriesSafe(pluginPath)
            .Where(path => !bytecodeCache.IsCacheDirectory(path))
            .ToHashSet(StringComparer.Ordinal);
        if (!actualDirectories.SetEquals(expectedDirectories))
        {
            throw new LilithRuntimeException(
                "The installed relationship-memory plugin contained unexpected directories; startup refused.");
        }
        var actual = Directory.EnumerateFiles(
                pluginPath,
                "*",
                SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(
                Path.GetRelativePath(pluginPath, path)))
            .Where(path => !bytecodeCache.IsCacheFile(path))
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            throw new LilithRuntimeException(
                "The installed relationship-memory plugin contained unexpected or missing files; startup refused.");
        }

        foreach (var entry in manifest.Files)
        {
            var path = ResolveContainedPath(pluginPath, entry.Path);
            var bytes = await ReadBoundedFileAsync(
                path,
                MaximumPluginFileBytes,
                "An installed relationship-memory plugin file was invalid.",
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    Sha256(bytes),
                    entry.Sha256,
                    StringComparison.Ordinal))
            {
                throw new LilithRuntimeException(
                    "The installed relationship-memory plugin drifted; startup refused without overwriting it.");
            }
        }

        var installedManifest = await ReadBoundedFileAsync(
            Path.Combine(pluginPath, ManifestFileName),
            MaximumManifestBytes,
            "The installed relationship-memory manifest was invalid.",
            cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                manifestBytes,
                installedManifest))
        {
            throw new LilithRuntimeException(
                "The installed relationship-memory manifest drifted; startup refused.");
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
            var filesProperty = root.GetProperty("files");
            if (filesProperty.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException();
            }

            var files = new List<PluginFile>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in filesProperty.EnumerateArray())
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
                {
                    throw new InvalidDataException();
                }
                files.Add(new PluginFile(path, sha256));
            }
            if (files.Count is < 2 or > MaximumPluginFiles)
            {
                throw new InvalidDataException();
            }
            return new PluginManifest(version, files.AsReadOnly());
        }
        catch (Exception exception) when (exception is JsonException
                                          or InvalidDataException
                                          or InvalidOperationException
                                          or FormatException
                                          or OverflowException)
        {
            throw new LilithRuntimeException(
                "The Lilith relationship-memory plugin manifest was rejected.");
        }
    }

    private static void RequireExactProperties(
        JsonElement value,
        params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.EnumerateObject().Select(property => property.Name)
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
            || text.Any(character => char.IsControl(character)))
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
            || normalized.Split('/').Any(segment =>
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
        if (!path.StartsWith(
                prefix,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "A relationship-memory plugin path escaped its root.");
        }
        return path;
    }

    private static IEnumerable<string> ParentDirectories(string relative)
    {
        var normalized = NormalizeRelativePath(relative);
        var segments = normalized.Split('/');
        for (var count = 1; count < segments.Length; count++)
        {
            yield return string.Join('/', segments.Take(count));
        }
    }

    private static HashSet<string> EnumerateDirectoriesSafe(string root)
    {
        const int maximumDirectories = 64;
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
                        "The installed relationship-memory plugin crossed a reparse point.");
                }
                var relative = NormalizeRelativePath(
                    Path.GetRelativePath(root, child.FullName));
                if (!result.Add(relative) || result.Count > maximumDirectories)
                {
                    throw new LilithRuntimeException(
                        "The installed relationship-memory plugin directory closure was invalid.");
                }
                pending.Push(child);
            }
        }
        return result;
    }

    private static void VerifyManagedDirectoryClosure(
        string managedDirectory,
        bool allowMissingConfig)
    {
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(
                    managedDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .ToArray();
            var expectedConfigPath = Path.Combine(
                managedDirectory,
                "config.yaml");
            foreach (var entry in entries)
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0
                    || (attributes & FileAttributes.Directory) != 0
                    || !string.Equals(
                        Path.GetFullPath(entry),
                        Path.GetFullPath(expectedConfigPath),
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    throw new LilithRuntimeException(
                        "The Runtime-managed configuration directory contained an unexpected entry; startup refused.");
                }
            }

            if ((!allowMissingConfig && entries.Length != 1)
                || (allowMissingConfig && entries.Length > 1))
            {
                throw new LilithRuntimeException(
                    "The Runtime-managed configuration directory closure was invalid; startup refused.");
            }
        }
        catch (LilithRuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(
                "The Runtime-managed configuration directory could not be verified; startup refused.");
        }
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
                    "The relationship-memory installation path crossed a reparse point.");
            }
            if (string.Equals(
                    current.FullName,
                    canonicalRoot,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return;
            }
            current = current.Parent;
        }
        throw new LilithRuntimeException(
            "The relationship-memory installation path escaped its profile.");
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

    private static async Task CreateOrMigrateManagedConfigAsync(
        string path,
        byte[] expected,
        string driftError,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            try
            {
                await WriteNewAsync(path, expected, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        var legacy = new UTF8Encoding(false).GetBytes(
            LegacyManagedConfigText);
        var actual = await ReadBoundedFileAsync(
            path,
            Math.Max(expected.Length, legacy.Length),
            driftError,
            cancellationToken).ConfigureAwait(false);
        if (CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return;
        }
        if (!CryptographicOperations.FixedTimeEquals(actual, legacy))
        {
            throw new LilithRuntimeException(driftError);
        }

        var temporaryPath = path + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            await WriteNewAsync(temporaryPath, expected, cancellationToken)
                .ConfigureAwait(false);
            var current = await ReadBoundedFileAsync(
                path,
                legacy.Length,
                driftError,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(current, legacy))
            {
                throw new LilithRuntimeException(
                    "The Runtime-owned relationship-memory configuration changed during migration; startup refused.");
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var migrated = await ReadBoundedFileAsync(
            path,
            expected.Length,
            driftError,
            cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(migrated, expected))
        {
            throw new LilithRuntimeException(
                "The Runtime-owned relationship-memory configuration migration could not be verified.");
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
           && value.All(character => character is >= '0' and <= '9'
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

    private sealed record PluginManifest(
        string Version,
        IReadOnlyList<PluginFile> Files);

    private sealed record PluginFile(string Path, string Sha256);
}
