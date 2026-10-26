using System;
using System.IO;
using System.Text;

namespace LilithTextInjector;

/// <summary>
/// File boundary for the privacy-minimized Codex lifecycle queue. It accepts
/// only direct JSON children of the configured events directory. A terminal
/// action first moves the exact source to a same-directory-volume, non-JSON
/// claim. Rejected items and conflicted claims remain recoverable.
/// </summary>
internal sealed class CodexBridgeQueueFileStore
    : ICodexBridgeQueueFileAccess, IDisposable
{
    private const int MaximumRecoverableFileNameBytes = 120;
    private readonly object _gate = new();
    private readonly string _eventsDirectory;
    private readonly string _quarantineDirectory;
    private readonly string _claimsDirectory;
    private readonly StringComparison _pathComparison;
    private FileStream? _instanceLease;
    private int _recoveryCursor;
    private bool _disposed;

    internal CodexBridgeQueueFileStore(
        string eventsDirectory,
        string quarantineDirectory)
    {
        _eventsDirectory = NormalizeDirectory(
            eventsDirectory,
            nameof(eventsDirectory));
        _quarantineDirectory = NormalizeDirectory(
            quarantineDirectory,
            nameof(quarantineDirectory));
        _claimsDirectory = Path.Combine(_eventsDirectory, ".claims");
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public CodexBridgeQueueReadResult Read(
        string queueItemIdentity,
        int maximumCharacters)
    {
        ValidateMaximumCharacters(maximumCharacters);
        lock (_gate)
        {
            if (!TryEnsureInstanceLease())
                return CodexBridgeQueueReadResult.Failure();
            if (!TryResolveQueueItem(queueItemIdentity, out var path))
                return CodexBridgeQueueReadResult.Failure();
            return ReadResolvedPath(path, maximumCharacters);
        }
    }

    public CodexBridgeQueueDeleteDisposition DeleteTerminal(
        string queueItemIdentity,
        CodexBridgeQueueSourceRevision expectedRevision)
    {
        if (expectedRevision == null)
            throw new ArgumentNullException(nameof(expectedRevision));
        if (!TryResolveQueueItem(queueItemIdentity, out var sourcePath))
        {
            return CodexBridgeQueueDeleteDisposition.TransientFailure;
        }

        lock (_gate)
        {
            if (!TryEnsureInstanceLease())
            {
                return CodexBridgeQueueDeleteDisposition.TransientFailure;
            }
            if (!TryClaimSource(sourcePath, out var claimPath))
                return ClassifyUnclaimedSource(sourcePath, expectedRevision);

            var current = ReadResolvedPath(
                claimPath,
                CodexBridgeQueueConsumer.MaximumJsonCharacters);
            if (!current.Succeeded || current.SourceRevision == null)
            {
                return ClassifyClaimResolution(
                    ResolveClaimAfterFailure(claimPath, sourcePath),
                    sourcePath,
                    expectedRevision);
            }
            if (!expectedRevision.Matches(current.SourceRevision))
            {
                ResolveClaimAfterFailure(claimPath, sourcePath);
                return CodexBridgeQueueDeleteDisposition.Superseded;
            }

            try
            {
                File.Delete(claimPath);
                return CodexBridgeQueueDeleteDisposition.Deleted;
            }
            catch
            {
                // The exact admitted source remains at the private claim path.
            }

            return ClassifyClaimResolution(
                ResolveClaimAfterFailure(claimPath, sourcePath),
                sourcePath,
                expectedRevision);
        }
    }

    internal CodexBridgeQueueQuarantineDisposition TryQuarantineRejected(
        string queueItemIdentity,
        CodexBridgeQueueReceipt receipt,
        CodexBridgeQueueQuarantinePermit permit)
    {
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));
        if (permit == null)
            throw new ArgumentNullException(nameof(permit));
        if (!permit.Authorizes(receipt)
            || !receipt.QuarantineEligible
            || receipt.Classification is not (
                CodexBridgeQueueClassification.Stale
                or CodexBridgeQueueClassification.Unsupported
                or CodexBridgeQueueClassification.Malformed))
        {
            return CodexBridgeQueueQuarantineDisposition.Unauthorized;
        }
        if (!TryResolveQueueItem(queueItemIdentity, out var sourcePath))
        {
            return CodexBridgeQueueQuarantineDisposition.TransientFailure;
        }

        lock (_gate)
        {
            if (!TryEnsureInstanceLease())
            {
                return CodexBridgeQueueQuarantineDisposition.TransientFailure;
            }
            if (!TryClaimSource(sourcePath, out var claimPath))
            {
                return ClassifyUnclaimedQuarantineSource(
                    sourcePath,
                    queueItemIdentity,
                    receipt);
            }

            var current = ReadResolvedPath(
                claimPath,
                CodexBridgeQueueConsumer.MaximumJsonCharacters);
            if (!current.Succeeded || current.SourceRevision == null)
            {
                return ClassifyQuarantineClaimResolution(
                    ResolveClaimAfterFailure(claimPath, sourcePath),
                    sourcePath,
                    queueItemIdentity,
                    receipt);
            }
            if (!string.Equals(
                    current.SourceRevision.CreateQueueItemFingerprint(
                        queueItemIdentity),
                    receipt.QueueItemFingerprint,
                    StringComparison.Ordinal))
            {
                ResolveClaimAfterFailure(claimPath, sourcePath);
                return CodexBridgeQueueQuarantineDisposition.Superseded;
            }

            var moved = TryMoveClaimToDisposition(
                claimPath,
                Path.GetFileName(sourcePath),
                receipt.Classification.ToString().ToLowerInvariant());
            if (moved)
                return CodexBridgeQueueQuarantineDisposition.Quarantined;

            var resolution = ResolveClaimAfterFailure(
                claimPath,
                sourcePath);
            return resolution == ClaimResolution.ConflictDisposed
                ? CodexBridgeQueueQuarantineDisposition.Quarantined
                : ClassifyQuarantineClaimResolution(
                    resolution,
                    sourcePath,
                    queueItemIdentity,
                    receipt);
        }
    }

    internal int RecoverAbandonedClaims(int maximumClaims = 32)
    {
        if (maximumClaims is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(maximumClaims));
        lock (_gate)
        {
            if (!TryEnsureInstanceLease())
                return 0;
            string[] claims;
            try
            {
                if (!Directory.Exists(_claimsDirectory))
                    return 0;
                claims = Directory.GetFiles(_claimsDirectory, "*.claim");
                Array.Sort(claims, StringComparer.Ordinal);
            }
            catch
            {
                return 0;
            }

            if (claims.Length == 0)
            {
                _recoveryCursor = 0;
                return 0;
            }

            var attempted = 0;
            var handled = 0;
            var start = _recoveryCursor % claims.Length;
            while (attempted < claims.Length
                && attempted < maximumClaims)
            {
                var index = (start + attempted) % claims.Length;
                var claimPath = claims[index];
                if (TryDecodeClaimFileName(claimPath, out var fileName))
                {
                    var sourcePath = Path.Combine(
                        _eventsDirectory,
                        fileName);
                    if (TryRestoreClaim(claimPath, sourcePath))
                    {
                        handled++;
                        attempted++;
                        continue;
                    }

                    if (TryMoveClaimToDisposition(
                        claimPath,
                        fileName,
                        "claim-conflict"))
                    {
                        handled++;
                    }
                }
                else
                {
                    if (TryMoveClaimToDisposition(
                        claimPath,
                        "orphan-" + Guid.NewGuid().ToString("N") + ".json",
                        "claim-conflict"))
                    {
                        handled++;
                    }
                }
                attempted++;
            }

            _recoveryCursor = (start + attempted) % claims.Length;
            return handled;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _instanceLease?.Dispose();
            _instanceLease = null;
        }
    }

    private bool TryEnsureInstanceLease()
    {
        lock (_gate)
        {
            if (_disposed)
                return false;
            if (_instanceLease != null)
                return true;

            try
            {
                Directory.CreateDirectory(_claimsDirectory);
                _instanceLease = new FileStream(
                    Path.Combine(_claimsDirectory, "bridge.instance.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return true;
            }
            catch
            {
                _instanceLease?.Dispose();
                _instanceLease = null;
                return false;
            }
        }
    }

    private static CodexBridgeQueueReadResult ReadResolvedPath(
        string path,
        int maximumCharacters)
    {
        ValidateMaximumCharacters(maximumCharacters);
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maximumCharacters + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = reader.Read(
                    buffer,
                    count,
                    buffer.Length - count);
                if (read == 0)
                    break;
                count += read;
            }
            return CodexBridgeQueueReadResult.Success(
                new string(buffer, 0, count));
        }
        catch
        {
            return CodexBridgeQueueReadResult.Failure();
        }
    }

    private bool TryClaimSource(string sourcePath, out string claimPath)
    {
        claimPath = string.Empty;
        try
        {
            var encodedFileName = EncodeRecoverableFileName(
                Path.GetFileName(sourcePath));
            Directory.CreateDirectory(_claimsDirectory);
            var claimIdentity = Guid.NewGuid().ToString("N");
            claimPath = Path.Combine(
                _claimsDirectory,
                encodedFileName == null
                    ? "unmapped-" + claimIdentity + ".claim"
                    : encodedFileName
                        + "."
                        + claimIdentity
                        + ".claim");
            File.Move(sourcePath, claimPath);
            return true;
        }
        catch
        {
            claimPath = string.Empty;
            return false;
        }
    }

    private ClaimResolution ResolveClaimAfterFailure(
        string claimPath,
        string sourcePath)
    {
        var initialPresence = GetClaimPresence(claimPath);
        if (initialPresence == ClaimPresence.Missing)
            return ClaimResolution.Missing;
        if (initialPresence == ClaimPresence.Unknown)
            return ClaimResolution.RetainedClaim;
        if (TryRestoreClaim(claimPath, sourcePath))
            return ClaimResolution.Restored;

        if (TryMoveClaimToDisposition(
                claimPath,
                Path.GetFileName(sourcePath),
                "claim-conflict"))
        {
            return ClaimResolution.ConflictDisposed;
        }

        return GetClaimPresence(claimPath) switch
        {
            ClaimPresence.Missing => ClaimResolution.Missing,
            _ => ClaimResolution.RetainedClaim
        };
    }

    private static CodexBridgeQueueDeleteDisposition
        ClassifyClaimResolution(
            ClaimResolution resolution,
            string sourcePath,
            CodexBridgeQueueSourceRevision expectedRevision)
        => resolution switch
        {
            ClaimResolution.Restored =>
                CodexBridgeQueueDeleteDisposition.TransientFailure,
            ClaimResolution.RetainedClaim =>
                CodexBridgeQueueDeleteDisposition.TransientFailure,
            ClaimResolution.ConflictDisposed =>
                CodexBridgeQueueDeleteDisposition.Superseded,
            _ => ClassifyUnclaimedSource(sourcePath, expectedRevision)
        };

    private static CodexBridgeQueueDeleteDisposition
        ClassifyUnclaimedSource(
            string sourcePath,
            CodexBridgeQueueSourceRevision expectedRevision)
    {
        var current = ReadResolvedPath(
            sourcePath,
            CodexBridgeQueueConsumer.MaximumJsonCharacters);
        if (current.Succeeded && current.SourceRevision != null)
        {
            return expectedRevision.Matches(current.SourceRevision)
                ? CodexBridgeQueueDeleteDisposition.TransientFailure
                : CodexBridgeQueueDeleteDisposition.Superseded;
        }

        return GetClaimPresence(sourcePath) == ClaimPresence.Missing
            ? CodexBridgeQueueDeleteDisposition.Superseded
            : CodexBridgeQueueDeleteDisposition.TransientFailure;
    }

    private static CodexBridgeQueueQuarantineDisposition
        ClassifyQuarantineClaimResolution(
            ClaimResolution resolution,
            string sourcePath,
            string queueItemIdentity,
            CodexBridgeQueueReceipt receipt)
        => resolution switch
        {
            ClaimResolution.Restored =>
                CodexBridgeQueueQuarantineDisposition.TransientFailure,
            ClaimResolution.RetainedClaim =>
                CodexBridgeQueueQuarantineDisposition.TransientFailure,
            ClaimResolution.ConflictDisposed =>
                CodexBridgeQueueQuarantineDisposition.Superseded,
            _ => ClassifyUnclaimedQuarantineSource(
                sourcePath,
                queueItemIdentity,
                receipt)
        };

    private static CodexBridgeQueueQuarantineDisposition
        ClassifyUnclaimedQuarantineSource(
            string sourcePath,
            string queueItemIdentity,
            CodexBridgeQueueReceipt receipt)
    {
        var current = ReadResolvedPath(
            sourcePath,
            CodexBridgeQueueConsumer.MaximumJsonCharacters);
        if (current.Succeeded && current.SourceRevision != null)
        {
            var currentFingerprint = current.SourceRevision
                .CreateQueueItemFingerprint(queueItemIdentity);
            return string.Equals(
                    currentFingerprint,
                    receipt.QueueItemFingerprint,
                    StringComparison.Ordinal)
                ? CodexBridgeQueueQuarantineDisposition.TransientFailure
                : CodexBridgeQueueQuarantineDisposition.Superseded;
        }

        return GetClaimPresence(sourcePath) == ClaimPresence.Missing
            ? CodexBridgeQueueQuarantineDisposition.Superseded
            : CodexBridgeQueueQuarantineDisposition.TransientFailure;
    }

    private static bool TryRestoreClaim(
        string claimPath,
        string sourcePath)
    {
        try
        {
            File.Move(claimPath, sourcePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ClaimPresence GetClaimPresence(string claimPath)
    {
        try
        {
            _ = File.GetAttributes(claimPath);
            return ClaimPresence.Present;
        }
        catch (FileNotFoundException)
        {
            return ClaimPresence.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return ClaimPresence.Missing;
        }
        catch
        {
            return ClaimPresence.Unknown;
        }
    }

    private bool TryMoveClaimToDisposition(
        string claimPath,
        string preferredFileName,
        string disposition)
    {
        try
        {
            var dispositionDirectory = Path.Combine(
                _quarantineDirectory,
                disposition);
            Directory.CreateDirectory(dispositionDirectory);
            var targetPath = UniqueTargetPath(
                dispositionDirectory,
                preferredFileName);
            File.Move(claimPath, targetPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string UniqueTargetPath(
        string directory,
        string preferredFileName)
    {
        var fileName = Path.GetFileName(preferredFileName);
        var targetPath = Path.Combine(directory, fileName);
        return !File.Exists(targetPath)
            ? targetPath
            : Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(fileName)
                    + "-"
                    + Guid.NewGuid().ToString("N")
                    + Path.GetExtension(fileName));
    }

    private static string? EncodeRecoverableFileName(string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes(fileName);
        if (bytes.Length is < 1 or > MaximumRecoverableFileNameBytes)
            return null;
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryDecodeClaimFileName(
        string claimPath,
        out string fileName)
    {
        fileName = string.Empty;
        try
        {
            var claimName = Path.GetFileName(claimPath);
            if (!claimName.EndsWith(".claim", StringComparison.Ordinal))
                return false;
            var withoutExtension = claimName[..^".claim".Length];
            var separator = withoutExtension.IndexOf('.');
            if (separator <= 0
                || !Guid.TryParseExact(
                    withoutExtension[(separator + 1)..],
                    "N",
                    out _))
            {
                return false;
            }

            var encoded = withoutExtension[..separator]
                .Replace('-', '+')
                .Replace('_', '/');
            encoded += (encoded.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty
            };
            fileName = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true)
                .GetString(Convert.FromBase64String(encoded));
            return string.Equals(
                    Path.GetFileName(fileName),
                    fileName,
                    StringComparison.Ordinal)
                && string.Equals(
                    Path.GetExtension(fileName),
                    ".json",
                    StringComparison.OrdinalIgnoreCase)
                && Encoding.UTF8.GetByteCount(fileName)
                    <= MaximumRecoverableFileNameBytes;
        }
        catch
        {
            fileName = string.Empty;
            return false;
        }
    }

    private bool TryResolveQueueItem(
        string? queueItemIdentity,
        out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(queueItemIdentity))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(queueItemIdentity);
            if (!string.Equals(
                    Path.GetDirectoryName(fullPath),
                    _eventsDirectory,
                    _pathComparison)
                || !string.Equals(
                    Path.GetExtension(fullPath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateMaximumCharacters(int maximumCharacters)
    {
        if (maximumCharacters <= 0 || maximumCharacters > 65_536)
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
    }

    private enum ClaimResolution
    {
        Missing = 0,
        Restored = 1,
        ConflictDisposed = 2,
        RetainedClaim = 3
    }

    private enum ClaimPresence
    {
        Unknown = 0,
        Present = 1,
        Missing = 2
    }

    private static string NormalizeDirectory(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A directory is required.", parameterName);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }
}
