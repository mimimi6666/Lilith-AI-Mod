using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal enum LilithRuntimeBootstrapDisposition
{
    LaunchRequested = 0,
    RuntimePayloadUnavailable = 1,
    LaunchFailed = 2
}

internal sealed record LilithRuntimeBootstrapReceipt(
    LilithRuntimeBootstrapDisposition Disposition,
    string Code)
{
    internal Task<LilithRuntimeStartupObservation>? StartupObservation { get; init; }

    internal bool LaunchWasRequested =>
        Disposition == LilithRuntimeBootstrapDisposition.LaunchRequested;
}

internal interface ILilithRuntimeBootstrap
{
    LilithRuntimeBootstrapReceipt EnsureStarted();
}

internal interface ILilithRuntimeProcessLauncher
{
    IDisposable? Start(ProcessStartInfo startInfo);
}

internal interface ILilithRuntimeStartupProcess : IDisposable
{
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed record LilithRuntimeStartupObservation(string Code, bool Failed)
{
    internal int? ExitCode { get; init; }

    internal static LilithRuntimeStartupObservation FromExitCode(int code)
        => new(LilithRuntimeStartupDiagnostics.CodeForExit(code),
            code is not (LilithRuntimeProcessExit.Normal or LilithRuntimeProcessExit.AlreadyRunning))
        { ExitCode = code };

    internal static async Task<LilithRuntimeStartupObservation> ObserveAsync(
        ILilithRuntimeStartupProcess process, TimeSpan timeout)
    {
        using (process)
        using (var lifetime = new CancellationTokenSource(timeout))
        {
            try
            {
                var code = await process.WaitForExitAsync(lifetime.Token)
                    .WaitAsync(lifetime.Token).ConfigureAwait(false);
                return FromExitCode(code);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // This is NOT evidence of readiness. The authenticated pipe
                // remains the only readiness authority; never kill or retry.
                return new("runtime.startup_observation_elapsed", false);
            }
            catch (Exception)
            {
                return new("runtime.startup_observation_unavailable", false);
            }
        }
    }
}

internal sealed class LilithRuntimeBootstrap : ILilithRuntimeBootstrap
{
    private static readonly string[] RequiredRuntimePayloadFiles =
    [
        "LilithRuntime.exe",
        "LilithRuntime.dll",
        "LilithRuntime.deps.json",
        "LilithRuntime.runtimeconfig.json",
        "LilithRuntimeContracts.dll",
        "LilithRuntimeProviderBridge.dll",
        LilithGameBridgeTrustManifest.ManifestFileName,
        LilithGameBridgeTrustManifest.DigestFileName
    ];

    private readonly string _runtimeDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, int, byte[]> _readBoundedFile;
    private readonly ILilithRuntimeProcessLauncher _processLauncher;
    private readonly Func<string?> _maintenanceBlock;

    private LilithRuntimeBootstrap(
        string controlCenterBaseDirectory,
        Func<string, bool> fileExists,
        Func<string, int, byte[]> readBoundedFile,
        ILilithRuntimeProcessLauncher processLauncher,
        Func<string?>? maintenanceBlock = null)
    {
        _runtimeDirectory = ResolveRuntimeDirectory(controlCenterBaseDirectory);
        _fileExists = fileExists
            ?? throw new ArgumentNullException(nameof(fileExists));
        _readBoundedFile = readBoundedFile
            ?? throw new ArgumentNullException(nameof(readBoundedFile));
        _processLauncher = processLauncher
            ?? throw new ArgumentNullException(nameof(processLauncher));
        _maintenanceBlock = maintenanceBlock ?? (() => null);
    }

    internal static ILilithRuntimeBootstrap ForCurrentDistribution()
        => new LilithRuntimeBootstrap(
            AppContext.BaseDirectory,
            File.Exists,
            ReadBoundedFile,
            SystemLilithRuntimeProcessLauncher.Instance,
            () => ControlCenterPlayerSaveState.BootstrapBlockCode);

    internal static LilithRuntimeBootstrap CreateForTesting(
        string controlCenterBaseDirectory,
        Func<string, bool> fileExists,
        Func<string, int, byte[]> readBoundedFile,
        ILilithRuntimeProcessLauncher processLauncher,
        Func<string?>? maintenanceBlock = null)
        => new(
            controlCenterBaseDirectory,
            fileExists,
            readBoundedFile,
            processLauncher,
            maintenanceBlock);

    internal static string ResolveRuntimeDirectory(
        string controlCenterBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(controlCenterBaseDirectory)
            || !Path.IsPathFullyQualified(controlCenterBaseDirectory))
        {
            throw new ArgumentException(
                "The Control Center installation directory must be absolute.",
                nameof(controlCenterBaseDirectory));
        }

        var controlCenterDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(controlCenterBaseDirectory));
        var distributionRoot = Directory.GetParent(controlCenterDirectory)
            ?.FullName
            ?? throw new ArgumentException(
                "The Control Center installation directory has no distribution root.",
                nameof(controlCenterBaseDirectory));
        var runtimeDirectory = Path.GetFullPath(
            Path.Combine(distributionRoot, "runtime"));
        var relative = Path.GetRelativePath(distributionRoot, runtimeDirectory);
        if (!string.Equals(relative, "runtime", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Runtime installation directory escaped the distribution root.");
        }

        return ResolveExistingDirectoryCasing(runtimeDirectory);
    }

    // Windows accepts a differently cased launch path, but the managed profile
    // deliberately hashes exact configuration bytes (including component paths).
    // Restore stored directory names without following links or changing scope.
    private static string ResolveExistingDirectoryCasing(string fullPath)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(fullPath))
            return fullPath;

        var root = Path.GetPathRoot(fullPath)!;
        var current = root.Length >= 2 && root[1] == ':'
            ? char.ToUpperInvariant(root[0]) + root[1..]
            : root;
        var parts = fullPath[root.Length..].Split(
            Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        try
        {
            foreach (var part in parts)
            {
                var matches = Directory.GetDirectories(current, part,
                    new EnumerationOptions
                    {
                        MatchCasing = MatchCasing.CaseInsensitive,
                        MatchType = MatchType.Simple,
                        AttributesToSkip = 0,
                        IgnoreInaccessible = false
                    });
                if (matches.Length != 1)
                    return fullPath; // Never guess an ambiguous or missing path.
                current = Path.Combine(current, Path.GetFileName(matches[0]));
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            return fullPath; // Existing payload and profile checks still fail closed.
        }
        return string.Equals(current, fullPath, StringComparison.OrdinalIgnoreCase)
            ? current : fullPath;
    }

    public LilithRuntimeBootstrapReceipt EnsureStarted()
    {
        if (_maintenanceBlock() is { } code)
            return new LilithRuntimeBootstrapReceipt(LilithRuntimeBootstrapDisposition.LaunchFailed, code);
        if (RequiredRuntimePayloadFiles.Any(file =>
                !_fileExists(Path.Combine(_runtimeDirectory, file))))
        {
            return new LilithRuntimeBootstrapReceipt(
                LilithRuntimeBootstrapDisposition.RuntimePayloadUnavailable,
                "runtime.payload_unavailable");
        }

        if (!TryReadPeerTrustBinding(
                out var peerTrustManifestPath,
                out var peerTrustManifestSha256))
        {
            return new LilithRuntimeBootstrapReceipt(
                LilithRuntimeBootstrapDisposition.RuntimePayloadUnavailable,
                "runtime.peer_trust_invalid");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(_runtimeDirectory, "LilithRuntime.exe"),
            WorkingDirectory = _runtimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("serve");
        startInfo.Environment[
            LilithGameBridgeTrustManifest.ManifestPathEnvironmentVariable] =
            peerTrustManifestPath;
        startInfo.Environment[
            LilithGameBridgeTrustManifest.ManifestSha256EnvironmentVariable] =
            peerTrustManifestSha256;

        try
        {
            var process = _processLauncher.Start(startInfo);
            if (process is null)
            {
                return new LilithRuntimeBootstrapReceipt(
                    LilithRuntimeBootstrapDisposition.LaunchFailed,
                    "runtime.launch_returned_no_process");
            }

            // Retain a handle only for a bounded asynchronous early-exit check.
            // Runtime still owns its single-instance lease and process lifetime.
            var observation = process is ILilithRuntimeStartupProcess observable
                ? LilithRuntimeStartupObservation.ObserveAsync(observable, TimeSpan.FromMinutes(2))
                : null;
            if (observation is null) process.Dispose();
            return new LilithRuntimeBootstrapReceipt(
                LilithRuntimeBootstrapDisposition.LaunchRequested,
                "runtime.launch_requested") { StartupObservation = observation };
        }
        catch (Exception exception) when (exception is Win32Exception
                                          or IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException)
        {
            return new LilithRuntimeBootstrapReceipt(
                LilithRuntimeBootstrapDisposition.LaunchFailed,
                "runtime.launch_failed");
        }
    }

    private bool TryReadPeerTrustBinding(
        out string manifestPath,
        out string manifestSha256)
    {
        manifestPath = Path.Combine(
            _runtimeDirectory,
            LilithGameBridgeTrustManifest.ManifestFileName);
        manifestSha256 = string.Empty;
        try
        {
            var manifestBytes = _readBoundedFile(
                manifestPath,
                LilithGameBridgeTrustManifest.MaximumManifestBytes);
            var digestBytes = _readBoundedFile(
                Path.Combine(
                    _runtimeDirectory,
                    LilithGameBridgeTrustManifest.DigestFileName),
                LilithGameBridgeTrustManifest.MaximumDigestFileBytes);
            var expectedSha256 =
                LilithGameBridgeTrustManifest.DecodeDigestFile(digestBytes);
            if (!string.Equals(
                    expectedSha256,
                    LilithGameBridgeTrustManifest.ComputeSha256(manifestBytes),
                    StringComparison.Ordinal))
            {
                return false;
            }
            _ = LilithGameBridgeTrustManifest.Decode(manifestBytes);
            manifestSha256 = expectedSha256;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or JsonException)
        {
            return false;
        }
    }

    private static byte[] ReadBoundedFile(string path, int maximumBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
            throw new InvalidDataException("A Runtime launch binding exceeded its bound.");
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed class SystemLilithRuntimeProcessLauncher
        : ILilithRuntimeProcessLauncher
    {
        internal static SystemLilithRuntimeProcessLauncher Instance { get; } =
            new();

        public IDisposable? Start(ProcessStartInfo startInfo)
        {
            var process = Process.Start(startInfo);
            return process is null ? null : new StartupProcess(process);
        }

        private sealed class StartupProcess(Process process) : ILilithRuntimeStartupProcess
        {
            public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return process.ExitCode;
            }

            public void Dispose() => process.Dispose();
        }
    }
}
