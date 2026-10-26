using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal static class PinnedHermesVoiceInstallation
{
    private const string ClosureHeader =
        "lilith-hermes-voice-closure-v1\n";

    internal static Task VerifyAsync(
        LilithRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identity = PinnedHermesVoiceLayout.IdentityForRootPath(options.VoiceComponentRootPath);
        return VerifyExactAsync(
            options.VoiceComponentRootPath,
            identity.EntryCount,
            identity.FileCount,
            identity.ClosureSha256,
            cancellationToken);
    }

    internal static async Task VerifyExactAsync(
        string rootPath,
        int expectedEntryCount,
        int expectedFileCount,
        string expectedClosureSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rootPath)
                || !Path.IsPathFullyQualified(rootPath)
                || expectedEntryCount <= 0
                || expectedFileCount <= 0
                || expectedFileCount > expectedEntryCount
                || expectedClosureSha256.Length != 64)
            {
                throw new LilithRuntimeException(
                    "The pinned Hermes voice component identity is invalid.");
            }

            LilithRuntimeWorkspacePathGuard.EnsureSafe(rootPath);
            DemandRegularDirectory(rootPath, "root");
            var entries = EnumerateWithoutReparseTraversal(rootPath, cancellationToken)
                .OrderBy(static entry => entry.RelativePath, StringComparer.Ordinal)
                .ToArray();
            if (entries.Length != expectedEntryCount)
            {
                throw new LilithRuntimeException(
                    "The pinned Hermes voice component closure does not match.");
            }

            var files = new List<BoundedComponentFileHashing.Input>(expectedFileCount);
            var directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry.FullPath);
                DemandNotReparsePoint(attributes, entry.RelativePath);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry.RelativePath);
                }
                else
                    files.Add(new BoundedComponentFileHashing.Input(entry.FullPath));
            }
            if (files.Count != expectedFileCount)
            {
                throw new LilithRuntimeException(
                    "The pinned Hermes voice component file closure does not match.");
            }

            var hashes = await BoundedComponentFileHashing.HashAsync(files, cancellationToken)
                .ConfigureAwait(false);
            using var canonical = new MemoryStream();
            await WriteUtf8Async(canonical, ClosureHeader, cancellationToken)
                .ConfigureAwait(false);
            var fileIndex = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (directories.Contains(entry.RelativePath))
                {
                    await WriteUtf8Async(canonical, "D\t" + entry.RelativePath + "\n", cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
                var hash = hashes[fileIndex++];
                await WriteUtf8Async(
                    canonical,
                    "F\t" + entry.RelativePath + "\t"
                    + hash.Length.ToString(CultureInfo.InvariantCulture) + "\t"
                    + hash.Sha256 + "\n",
                    cancellationToken).ConfigureAwait(false);
            }

            canonical.Position = 0;
            var actualClosureSha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(canonical, cancellationToken)
                    .ConfigureAwait(false));
            if (!string.Equals(
                    actualClosureSha256,
                    expectedClosureSha256,
                    StringComparison.Ordinal))
            {
                throw new LilithRuntimeException(
                    "The pinned Hermes voice component closure hash does not match.");
            }
        }
        catch (LilithRuntimeException exception)
        {
            throw LilithRuntimeStartupFailure.Classify(exception,
                LilithRuntimeFailureKind.VoiceComponentVerificationFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            throw new LilithRuntimeException(
                "The pinned Hermes voice component is missing or unreadable.",
                LilithRuntimeFailureKind.VoiceComponentVerificationFailed);
        }
    }

    internal static void DemandNotReparsePoint(
        FileAttributes attributes,
        string component)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new LilithRuntimeException(
                $"The pinned Hermes voice component '{component}' must not be a reparse point.");
        }
    }

    private static void DemandRegularDirectory(string path, string component)
    {
        var attributes = File.GetAttributes(path);
        DemandNotReparsePoint(attributes, component);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new LilithRuntimeException(
                "The pinned Hermes voice component root is not a directory.");
        }
    }

    private static IReadOnlyList<ComponentEntry> EnumerateWithoutReparseTraversal(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var entries = new List<ComponentEntry>();
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = NormalizeRelativePath(rootPath, path);
                var attributes = File.GetAttributes(path);
                DemandNotReparsePoint(attributes, relativePath);
                entries.Add(new ComponentEntry(path, relativePath));
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
            }
        }
        return entries;
    }

    private static string NormalizeRelativePath(string rootPath, string path)
    {
        var relative = Path.GetRelativePath(rootPath, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (relative.Length == 0
            || Path.IsPathFullyQualified(relative)
            || relative is "." or ".."
            || relative.StartsWith("../", StringComparison.Ordinal)
            || relative.Any(char.IsControl))
        {
            throw new LilithRuntimeException(
                "The pinned Hermes voice component contains an invalid path.");
        }
        return relative;
    }

    private static async Task WriteUtf8Async(
        Stream stream,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private sealed record ComponentEntry(string FullPath, string RelativePath);
}
