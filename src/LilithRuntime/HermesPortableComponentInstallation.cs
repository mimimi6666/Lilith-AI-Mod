using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal static class HermesPortableComponentInstallation
{
    internal const string ManifestFileName =
        "HermesComponent.manifest.json";

    internal const string AuthorityResourceName =
        "LilithRuntime.HermesComponentAuthority.v1.json";

    private const string ManifestSchema =
        "lilith-hermes-portable-component/v1";

    private const string ClosureHeader =
        "lilith-hermes-portable-closure-v1\n";

    private const int MaximumManifestBytes = 16 * 1024 * 1024;

    private static readonly HashSet<string> ReservedWindowsNames = new(
        new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
            "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
            "LPT8", "LPT9"
        },
        StringComparer.OrdinalIgnoreCase);

    internal static Task VerifyAsync(
        LilithRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return VerifyExactAsync(
            options.HermesComponentRootPath,
            ReadEmbeddedAuthority(),
            cancellationToken);
    }

    internal static async Task VerifyExactAsync(
        string rootPath,
        ReadOnlyMemory<byte> authorityBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rootPath)
                || !Path.IsPathFullyQualified(rootPath)
                || authorityBytes.IsEmpty
                || authorityBytes.Length > MaximumManifestBytes)
            {
                throw InvalidIdentity();
            }

            var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(rootPath));
            DemandRegularDirectoryChain(root);

            var manifestPath = Path.Combine(root, ManifestFileName);
            DemandRegularFile(manifestPath, ManifestFileName);
            var manifestBytes = await ReadBoundedFileAsync(
                manifestPath,
                MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    manifestBytes,
                    authorityBytes.Span))
            {
                throw new LilithRuntimeException(
                    "The packaged Hermes manifest does not match Runtime authority.",
                    LilithRuntimeFailureKind.HermesComponentHashMismatch);
            }

            var manifest = DecodeManifest(authorityBytes);
            VerifyProductIdentity(manifest);
            await VerifyClosureAsync(root, manifest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LilithRuntimeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
                                           or FormatException
                                           or OverflowException)
        {
            throw InvalidIdentity();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or System.Security.SecurityException)
        {
            throw new LilithRuntimeException(
                "The packaged Hermes component is missing, unreadable, or invalid.",
                LilithRuntimeFailureKind.HermesComponentMissingOrUnreadable);
        }
    }

    private static byte[] ReadEmbeddedAuthority()
    {
        using var stream = typeof(HermesPortableComponentInstallation)
            .Assembly
            .GetManifestResourceStream(AuthorityResourceName)
            ?? throw new LilithRuntimeException(
                "Runtime has no embedded Hermes component authority.",
                LilithRuntimeFailureKind.HermesComponentLayoutMismatch);
        if (stream.Length is <= 0 or > MaximumManifestBytes)
            throw InvalidIdentity();
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static PortableManifest DecodeManifest(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
        var root = document.RootElement;
        DemandObjectProperties(
            root,
            "schemaVersion",
            "componentVersion",
            "hermesCommit",
            "target",
            "entryCount",
            "fileCount",
            "fileBytes",
            "closureSha256",
            "entries");
        var target = root.GetProperty("target");
        DemandObjectProperties(
            target,
            "os",
            "architecture",
            "pythonAbi",
            "pythonVersion",
            "hermesVersion");

        var entriesElement = root.GetProperty("entries");
        if (entriesElement.ValueKind != JsonValueKind.Array)
            throw InvalidIdentity();
        var entries = new List<PortableEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        foreach (var element in entriesElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw InvalidIdentity();
            var type = RequiredString(element, "type");
            PortableEntry entry;
            if (string.Equals(type, "directory", StringComparison.Ordinal))
            {
                DemandObjectProperties(element, "type", "path");
                entry = new PortableEntry(
                    "directory",
                    RequiredString(element, "path"),
                    0,
                    string.Empty);
            }
            else if (string.Equals(type, "file", StringComparison.Ordinal))
            {
                DemandObjectProperties(element, "type", "path", "length", "sha256");
                entry = new PortableEntry(
                    "file",
                    RequiredString(element, "path"),
                    RequiredInt64(element, "length", minimum: 0),
                    RequiredSha256(element, "sha256"));
            }
            else
            {
                throw InvalidIdentity();
            }

            DemandCanonicalRelativePath(entry.Path);
            DemandAllowedPayloadPath(entry.Path);
            if (!seen.Add(entry.Path)
                || previous is not null
                    && StringComparer.Ordinal.Compare(previous, entry.Path) >= 0)
            {
                throw InvalidIdentity();
            }
            previous = entry.Path;
            entries.Add(entry);
        }

        var manifest = new PortableManifest(
            RequiredString(root, "schemaVersion"),
            RequiredString(root, "componentVersion"),
            RequiredString(root, "hermesCommit"),
            RequiredString(target, "os"),
            RequiredString(target, "architecture"),
            RequiredString(target, "pythonAbi"),
            RequiredString(target, "pythonVersion"),
            RequiredString(target, "hermesVersion"),
            RequiredInt32(root, "entryCount", minimum: 1),
            RequiredInt32(root, "fileCount", minimum: 1),
            RequiredInt64(root, "fileBytes", minimum: 1),
            RequiredSha256(root, "closureSha256"),
            entries.AsReadOnly());
        DemandManifestTopology(manifest);
        return manifest;
    }

    private static void VerifyProductIdentity(PortableManifest manifest)
    {
        if (!string.Equals(manifest.SchemaVersion, ManifestSchema, StringComparison.Ordinal)
            || !string.Equals(
                manifest.ComponentVersion,
                LilithRuntimeOptions.PinnedHermesComponentVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.HermesCommit,
                LilithRuntimeOptions.PinnedHermesCommit,
                StringComparison.Ordinal)
            || !string.Equals(manifest.OperatingSystem, "windows", StringComparison.Ordinal)
            || !string.Equals(manifest.Architecture, "x86_64", StringComparison.Ordinal)
            || !string.Equals(manifest.PythonAbi, "cp311", StringComparison.Ordinal)
            || !string.Equals(manifest.PythonVersion, "3.11.15", StringComparison.Ordinal)
            || !string.Equals(manifest.HermesVersion, "0.20.1", StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "The packaged Hermes component identity does not match Runtime pins.",
                LilithRuntimeFailureKind.HermesComponentLayoutMismatch);
        }
    }

    private static async Task VerifyClosureAsync(
        string root,
        PortableManifest manifest,
        CancellationToken cancellationToken)
    {
        var actual = new Dictionary<string, ActualEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateWithoutReparseTraversal(root, cancellationToken))
        {
            if (string.Equals(
                    entry.RelativePath,
                    ManifestFileName,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (!actual.TryAdd(entry.RelativePath, entry))
                throw ClosureMismatch();
        }
        if (actual.Count != manifest.EntryCount
            || manifest.Entries.Count != manifest.EntryCount
            || manifest.FileCount > manifest.EntryCount)
        {
            throw ClosureMismatch();
        }

        var files = new List<BoundedComponentFileHashing.Input>(manifest.FileCount);
        foreach (var expected in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!actual.Remove(expected.Path, out var entry)
                || !string.Equals(
                    entry.RelativePath,
                    expected.Path,
                    StringComparison.Ordinal))
            {
                throw ClosureMismatch();
            }

            var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
            if (expected.IsDirectory != isDirectory)
                throw ClosureMismatch();
            if (!isDirectory)
            {
                if (new FileInfo(entry.FullPath).Length != expected.Length)
                    throw HashMismatch();
                files.Add(new BoundedComponentFileHashing.Input(entry.FullPath, expected.Length));
            }
        }
        if (actual.Count != 0 || files.Count != manifest.FileCount)
            throw ClosureMismatch();

        var hashes = await BoundedComponentFileHashing.HashAsync(files, cancellationToken)
            .ConfigureAwait(false);
        using var canonical = new MemoryStream();
        await WriteUtf8Async(canonical, ClosureHeader, cancellationToken)
            .ConfigureAwait(false);
        var fileCount = 0;
        long fileBytes = 0;
        foreach (var expected in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expected.IsDirectory)
            {
                await WriteUtf8Async(canonical, "D\t" + expected.Path + "\n", cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }
            var hash = hashes[fileCount++];
            if (hash.Length != expected.Length
                || !string.Equals(hash.Sha256, expected.Sha256, StringComparison.Ordinal))
                throw HashMismatch();
            fileBytes = checked(fileBytes + hash.Length);
            await WriteUtf8Async(
                canonical,
                "F\t" + expected.Path + "\t"
                + expected.Length.ToString(CultureInfo.InvariantCulture) + "\t"
                + expected.Sha256 + "\n",
                cancellationToken).ConfigureAwait(false);
        }

        if (actual.Count != 0
            || fileCount != manifest.FileCount
            || fileBytes != manifest.FileBytes)
        {
            throw HashMismatch();
        }
        canonical.Position = 0;
        var closureSha256 = Convert.ToHexString(
            await SHA256.HashDataAsync(canonical, cancellationToken)
                .ConfigureAwait(false));
        if (!string.Equals(
                closureSha256,
                manifest.ClosureSha256,
                StringComparison.Ordinal))
        {
            throw HashMismatch();
        }
    }

    private static void DemandManifestTopology(PortableManifest manifest)
    {
        if (manifest.EntryCount != manifest.Entries.Count
            || manifest.FileCount != manifest.Entries.Count(static entry => !entry.IsDirectory)
            || manifest.FileBytes != manifest.Entries
                .Where(static entry => !entry.IsDirectory)
                .Sum(static entry => entry.Length))
        {
            throw InvalidIdentity();
        }

        var entries = manifest.Entries.ToDictionary(
            static entry => entry.Path,
            StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            var slash = entry.Path.LastIndexOf('/');
            if (slash >= 0)
            {
                var parent = entry.Path[..slash];
                if (!entries.TryGetValue(parent, out var parentEntry)
                    || !parentEntry.IsDirectory)
                {
                    throw InvalidIdentity();
                }
            }
        }

        foreach (var requiredDirectory in new[]
                 {
                     "python",
                     "source",
                     "source/agent",
                     "source/assets",
                     "source/gateway",
                     "source/gateway/assets",
                     "source/hermes_cli",
                     "source/locales",
                     "source/plugins",
                     "source/skills",
                     "source/tools",
                     "source/tui_gateway",
                     "site-packages",
                     "site-packages/pythonwin",
                     "site-packages/win32",
                     "site-packages/win32/lib",
                     "site-packages/pywin32_system32"
                 })
        {
            if (!entries.TryGetValue(requiredDirectory, out var entry)
                || !entry.IsDirectory)
            {
                throw InvalidIdentity();
            }
        }

        foreach (var requiredFile in new[]
                 {
                     "python/python.exe",
                     "python/python311.dll",
                     "source/.hermes_build_sha",
                     "source/LICENSE",
                     "source/sitecustomize.py",
                     "source/agent/__init__.py",
                     "source/gateway/run.py",
                     "source/hermes_cli/main.py",
                     "source/hermes_cli/web_server.py",
                     "source/hermes_cli/data/plugin_index.json",
                     "source/tools/registry.py",
                     "source/tui_gateway/server.py",
                     "site-packages/pywin32.pth",
                     "site-packages/fastapi/__init__.py",
                     "site-packages/pythoncom.py",
                     "site-packages/uvicorn/__init__.py",
                     "site-packages/win32/win32api.pyd",
                     "site-packages/win32/win32file.pyd",
                     "site-packages/win32/win32security.pyd",
                     "site-packages/win32/lib/pywintypes.py",
                     "site-packages/pywin32_system32/pythoncom311.dll",
                     "site-packages/pywin32_system32/pywintypes311.dll"
                 })
        {
            if (!entries.TryGetValue(requiredFile, out var entry)
                || entry.IsDirectory)
            {
                throw InvalidIdentity();
            }
        }
    }

    private static void DemandAllowedPayloadPath(string path)
    {
        var wrapped = "/" + path.ToLowerInvariant() + "/";
        if (wrapped.Contains("/.git/", StringComparison.Ordinal)
            || wrapped.Contains("/__pycache__/", StringComparison.Ordinal)
            || wrapped.Contains("/.pytest_cache/", StringComparison.Ordinal)
            || wrapped.Contains("/.pytest-cache/", StringComparison.Ordinal)
            || wrapped.Contains("/.ruff_cache/", StringComparison.Ordinal)
            || wrapped.Contains("/__editable__", StringComparison.Ordinal)
            || wrapped.Contains("/_virtualenv.py/", StringComparison.Ordinal)
            || wrapped.Contains("/_virtualenv.pth/", StringComparison.Ordinal)
            || wrapped.EndsWith(".pyc/", StringComparison.Ordinal)
            || wrapped.EndsWith(".pyo/", StringComparison.Ordinal)
            || wrapped.EndsWith(".egg-link/", StringComparison.Ordinal)
            || wrapped.EndsWith("/direct_url.json/", StringComparison.Ordinal)
            || wrapped.EndsWith("/pyvenv.cfg/", StringComparison.Ordinal)
            || wrapped.StartsWith("/python/lib/site-packages/", StringComparison.Ordinal))
        {
            throw InvalidIdentity();
        }

        if (wrapped.EndsWith(".pth/", StringComparison.Ordinal)
            && !string.Equals(
                path,
                "site-packages/pywin32.pth",
                StringComparison.Ordinal))
        {
            throw InvalidIdentity();
        }
    }

    private static IReadOnlyList<ActualEntry> EnumerateWithoutReparseTraversal(
        string root,
        CancellationToken cancellationToken)
    {
        var result = new List<ActualEntry>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                DemandCanonicalRelativePath(relative);
                var attributes = File.GetAttributes(path);
                DemandNotReparse(attributes, relative);
                result.Add(new ActualEntry(path, relative, attributes));
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
            }
        }
        return result;
    }

    private static void DemandRegularDirectoryChain(string root)
    {
        var current = new DirectoryInfo(root);
        if (!current.Exists)
            throw new LilithRuntimeException(
                "The packaged Hermes component directory is missing.",
                LilithRuntimeFailureKind.HermesComponentMissingOrUnreadable);
        while (current is not null)
        {
            DemandNotReparse(current.Attributes, current.FullName);
            current = current.Parent;
        }
    }

    private static void DemandRegularFile(string path, string component)
    {
        var attributes = File.GetAttributes(path);
        DemandNotReparse(attributes, component);
        if ((attributes & FileAttributes.Directory) != 0)
            throw InvalidIdentity();
    }

    private static void DemandNotReparse(
        FileAttributes attributes,
        string component)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new LilithRuntimeException(
                $"The packaged Hermes component '{component}' crossed a reparse point.",
                LilithRuntimeFailureKind.HermesComponentLayoutMismatch);
        }
    }

    private static void DemandCanonicalRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path)
            || path.Length > 1024
            || path[0] == '/'
            || path[^1] == '/'
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal)
            || path.Contains("//", StringComparison.Ordinal)
            || path.Any(char.IsControl))
        {
            throw InvalidIdentity();
        }
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0
                || segment is "." or ".."
                || segment.EndsWith(' ')
                || segment.EndsWith('.')
                || ReservedWindowsNames.Contains(
                    segment.Split('.', 2, StringSplitOptions.None)[0]))
            {
                throw InvalidIdentity();
            }
        }
    }

    private static void DemandObjectProperties(
        JsonElement element,
        params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw InvalidIdentity();
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expectedSet.Contains(property.Name) || !seen.Add(property.Name))
                throw InvalidIdentity();
        }
        if (seen.Count != expectedSet.Count)
            throw InvalidIdentity();
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(value))
            throw InvalidIdentity();
        return value;
    }

    private static string RequiredSha256(JsonElement element, string name)
    {
        var value = RequiredString(element, name);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'A' and <= 'F')))
        {
            throw InvalidIdentity();
        }
        return value;
    }

    private static int RequiredInt32(
        JsonElement element,
        string name,
        int minimum)
    {
        var property = element.GetProperty(name);
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value < minimum)
        {
            throw InvalidIdentity();
        }
        return value;
    }

    private static long RequiredInt64(
        JsonElement element,
        string name,
        long minimum)
    {
        var property = element.GetProperty(name);
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var value)
            || value < minimum)
        {
            throw InvalidIdentity();
        }
        return value;
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
            throw InvalidIdentity();
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private static async Task WriteUtf8Async(
        Stream stream,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static LilithRuntimeException InvalidIdentity()
        => new("The packaged Hermes component identity is invalid.",
            LilithRuntimeFailureKind.HermesComponentLayoutMismatch);

    private static LilithRuntimeException ClosureMismatch()
        => new("The packaged Hermes component closure does not match.",
            LilithRuntimeFailureKind.HermesComponentLayoutMismatch);

    private static LilithRuntimeException HashMismatch()
        => new("The packaged Hermes component closure does not match.",
            LilithRuntimeFailureKind.HermesComponentHashMismatch);

    private sealed record PortableManifest(
        string SchemaVersion,
        string ComponentVersion,
        string HermesCommit,
        string OperatingSystem,
        string Architecture,
        string PythonAbi,
        string PythonVersion,
        string HermesVersion,
        int EntryCount,
        int FileCount,
        long FileBytes,
        string ClosureSha256,
        IReadOnlyList<PortableEntry> Entries);

    private sealed record PortableEntry(
        string Type,
        string Path,
        long Length,
        string Sha256)
    {
        internal bool IsDirectory =>
            string.Equals(Type, "directory", StringComparison.Ordinal);
    }

    private sealed record ActualEntry(
        string FullPath,
        string RelativePath,
        FileAttributes Attributes);
}
