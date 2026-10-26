using System.Security.Cryptography;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal static class PinnedCuaDriverInstallation
{
    internal static IReadOnlyDictionary<string, string> PinnedFiles { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cua-driver.exe"] = LilithRuntimeOptions.PinnedCuaDriverSha256,
            ["cua-driver-uia.exe"] =
                "FB8E15264973FC61F66F821A49290EF9E7303D203D12FCA027D92226387FFF37",
            ["cua-cursor-theme.exe"] =
                "A5246BE025C48BAE0354B848631E7FE4DB43E5921CCC2593833D7B6739E3912B",
            ["cua_driver_sdk.dll"] =
                "5BB7432E44EBF5474A4E655E7B447E06F8B3FAA4B69FDA4FBB12B6D37E117EBA",
            ["cua_driver_node_runtime.node"] =
                "F98D2370B2A16065860E72624A59C6DAB26ADE7B5CC2EA688CE756DE36BE31EC",
            ["cua_driver_abi.h"] =
                "C17169F41DA321BAA5E7E953323C3AD660B00790176BA381E93189FBA3506587"
        };

    internal static Task VerifyAsync(
        LilithRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return VerifyExactAsync(
            options.CuaDriverExecutablePath,
            PinnedFiles,
            cancellationToken);
    }

    internal static async Task VerifyExactAsync(
        string executablePath,
        IReadOnlyDictionary<string, string> expectedFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.Equals(
                    Path.GetFileName(executablePath),
                    "cua-driver.exe",
                    StringComparison.Ordinal))
            {
                throw new LilithRuntimeException(
                    "The pinned cua-driver executable name does not match.");
            }

            var directoryPath = Path.GetDirectoryName(executablePath)
                ?? throw new LilithRuntimeException(
                    "The pinned cua-driver directory is unavailable.");
            DemandNotReparsePoint(
                File.GetAttributes(directoryPath),
                "directory");

            var observedNames = Directory
                .EnumerateFileSystemEntries(directoryPath)
                .Select(Path.GetFileName)
                .ToArray();
            if (observedNames.Length != expectedFiles.Count
                || observedNames.Any(static name => name is null)
                || !expectedFiles.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(observedNames!))
            {
                throw new LilithRuntimeException(
                    "The pinned cua-driver directory contents do not match.");
            }

            foreach (var expected in expectedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(directoryPath, expected.Key);
                var attributes = File.GetAttributes(path);
                DemandNotReparsePoint(attributes, expected.Key);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    throw new LilithRuntimeException(
                        "A pinned cua-driver component is not a regular file.");
                }

                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var actual = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken)
                        .ConfigureAwait(false));
                if (!string.Equals(
                        actual,
                        expected.Value,
                        StringComparison.Ordinal))
                {
                    throw new LilithRuntimeException(
                        "A pinned cua-driver component hash does not match.");
                }
            }
        }
        catch (LilithRuntimeException exception)
        {
            throw LilithRuntimeStartupFailure.Classify(exception,
                LilithRuntimeFailureKind.CuaComponentVerificationFailed);
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
                "The pinned cua-driver executable is missing or unreadable.",
                LilithRuntimeFailureKind.CuaComponentVerificationFailed);
        }
    }

    internal static void DemandNotReparsePoint(
        FileAttributes attributes,
        string component)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new LilithRuntimeException(
                $"The pinned cua-driver {component} must not be a reparse point.");
        }
    }
}
