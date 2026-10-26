using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LilithProviderControlPlane;

internal enum UnifiedProviderCurrentMetadataAdmission
{
    Invalid = 0,
    Accepted = 1,
    UnsupportedPlatform = 2,
    InvalidRoot = 3,
    Missing = 4,
    NonFixedLocalVolume = 5,
    ReparseRejected = 6,
    FinalPathMismatch = 7,
    SecurityRejected = 8,
    FileIdentityRejected = 9,
    HardLinkRejected = 10,
    NamedStreamRejected = 11,
    OutOfBounds = 12,
    Corrupt = 13,
    ChangedDuringRead = 14,
    StorageFailure = 15
}

internal enum UnifiedProviderOpaqueCredentialReferenceState
{
    Invalid = 0,
    NotConfigured = 1,
    DeclaredNotHealthChecked = 2
}

internal sealed class UnifiedProviderCurrentMetadataResult
{
    internal UnifiedProviderCurrentMetadataAdmission Admission { get; }
    internal bool Accepted =>
        Admission == UnifiedProviderCurrentMetadataAdmission.Accepted;
    internal UnifiedProviderCurrentMetadataProjection? Projection { get; }

    private UnifiedProviderCurrentMetadataResult(
        UnifiedProviderCurrentMetadataAdmission admission,
        UnifiedProviderCurrentMetadataProjection? projection)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderCurrentMetadataAdmission), admission)
            || admission == UnifiedProviderCurrentMetadataAdmission.Invalid
            || (admission == UnifiedProviderCurrentMetadataAdmission.Accepted)
                != (projection != null))
        {
            throw new ArgumentException(
                "The current-metadata result was incoherent.",
                nameof(admission));
        }
        Admission = admission;
        Projection = projection;
    }

    internal static UnifiedProviderCurrentMetadataResult Accept(
        UnifiedProviderCurrentMetadataProjection projection)
        => new(
            UnifiedProviderCurrentMetadataAdmission.Accepted,
            projection ?? throw new ArgumentNullException(nameof(projection)));

    internal static UnifiedProviderCurrentMetadataResult Stop(
        UnifiedProviderCurrentMetadataAdmission admission)
        => new(admission, null);
}

/// <summary>
/// Sanitized Desired-only facts from exactly one D-033 current snapshot.  This
/// type intentionally contains no endpoint, workspace, file path, credential
/// value, effective-runtime claim, or caller-supplied authority.
/// </summary>
internal sealed class UnifiedProviderCurrentMetadataProjection
{
    internal string CodecIdentity { get; }
    internal int SchemaVersion { get; }
    internal long StoreGeneration { get; }
    internal string SnapshotFingerprint { get; }
    internal string ManifestSha256 { get; }
    internal string PayloadSha256 { get; }
    internal long ManifestCreatedAtUnixMilliseconds { get; }
    internal string MetadataAuthorityFingerprint { get; }
    internal string DesiredOwnerIdentity { get; }
    internal string DesiredOwnerFingerprint { get; }
    internal bool DesiredAssignmentAvailable { get; }
    internal int DesiredProviderKind { get; }
    internal string DesiredProfileId { get; }
    internal int DesiredProtocolKind { get; }
    internal string DesiredProtocolFingerprint { get; }
    internal string DesiredInstanceFingerprint { get; }
    internal string DesiredTextAssignmentFingerprint { get; }
    internal string DesiredModel { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal long CatalogObservedAtUnixMilliseconds { get; }
    internal long CatalogExpiresAtUnixMilliseconds { get; }
    internal string CatalogFingerprint { get; }
    internal string RoutePolicyFingerprint { get; }
    internal string EndpointAuthorityFingerprint { get; }
    internal UnifiedProviderOpaqueCredentialReferenceState
        CredentialReferenceState { get; }
    internal string CredentialReferenceFingerprint { get; }
    internal bool EffectiveAuthorityAvailable => false;

    internal UnifiedProviderCurrentMetadataProjection(
        long storeGeneration,
        string snapshotFingerprint,
        string manifestSha256,
        string payloadSha256,
        long manifestCreatedAtUnixMilliseconds,
        string metadataAuthorityFingerprint,
        UnifiedProviderControlPlaneCanonicalInstance? desired)
    {
        CodecIdentity =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec.CodecIdentity;
        SchemaVersion =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec.CurrentSchemaVersion;
        if (storeGeneration <= 0
            || !UnifiedProviderControlPlaneCanonicalSnapshotCodec.IsSha256(
                snapshotFingerprint)
            || !UnifiedProviderControlPlaneCanonicalSnapshotCodec.IsSha256(
                manifestSha256)
            || !UnifiedProviderControlPlaneCanonicalSnapshotCodec.IsSha256(
                payloadSha256)
            || !UnifiedProviderControlPlaneCanonicalSnapshotCodec.IsSha256(
                metadataAuthorityFingerprint))
        {
            throw new ArgumentException(
                "The current-metadata projection identity was invalid.");
        }
        StoreGeneration = storeGeneration;
        SnapshotFingerprint = snapshotFingerprint;
        ManifestSha256 = manifestSha256;
        PayloadSha256 = payloadSha256;
        ManifestCreatedAtUnixMilliseconds = manifestCreatedAtUnixMilliseconds;
        MetadataAuthorityFingerprint = metadataAuthorityFingerprint;
        DesiredOwnerIdentity =
            UnifiedProviderCurrentMetadataAuthority.DesiredOwnerIdentity;
        DesiredOwnerFingerprint =
            UnifiedProviderCurrentMetadataAuthority.DesiredOwnerFingerprint;
        DesiredAssignmentAvailable = desired != null;
        if (desired == null)
        {
            DesiredProfileId = string.Empty;
            DesiredProtocolFingerprint = string.Empty;
            DesiredInstanceFingerprint = string.Empty;
            DesiredTextAssignmentFingerprint = string.Empty;
            DesiredModel = string.Empty;
            ConfigurationFingerprint = string.Empty;
            CatalogFingerprint = string.Empty;
            RoutePolicyFingerprint = string.Empty;
            EndpointAuthorityFingerprint = string.Empty;
            CredentialReferenceState =
                UnifiedProviderOpaqueCredentialReferenceState.NotConfigured;
            CredentialReferenceFingerprint = string.Empty;
            return;
        }

        DesiredProviderKind = desired.ProviderKind;
        DesiredProfileId = desired.ProfileId;
        DesiredProtocolKind = ProtocolKind(desired.ProviderKind);
        DesiredProtocolFingerprint =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                "D033/CURRENT-METADATA/PROTOCOL/1",
                DesiredProviderKind.ToString(CultureInfo.InvariantCulture),
                DesiredProfileId,
                DesiredProtocolKind.ToString(CultureInfo.InvariantCulture));
        DesiredInstanceFingerprint =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                "D033/CURRENT-METADATA/INSTANCE/1",
                desired.InstanceId);
        DesiredTextAssignmentFingerprint =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                "D033/CURRENT-METADATA/DESIRED-TEXT-ASSIGNMENT/1",
                DesiredInstanceFingerprint,
                desired.SelectedModel,
                desired.ConfigurationGeneration.ToString(
                    CultureInfo.InvariantCulture),
                UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .ComputeCurrentConfigurationFingerprint(desired));
        DesiredModel = desired.SelectedModel;
        ConfigurationGeneration = desired.ConfigurationGeneration;
        ConfigurationFingerprint =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .ComputeCurrentConfigurationFingerprint(desired);
        CatalogObservedAtUnixMilliseconds =
            desired.CatalogObservedAtUnixMilliseconds;
        CatalogExpiresAtUnixMilliseconds =
            desired.CatalogExpiresAtUnixMilliseconds;
        CatalogFingerprint = desired.CatalogFingerprint;
        RoutePolicyFingerprint =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .ComputeCurrentRouteFingerprint(desired);
        EndpointAuthorityFingerprint =
            UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .ComputeEndpointAuthorityFingerprint(desired);
        CredentialReferenceState = desired.CredentialConfigured
            ? UnifiedProviderOpaqueCredentialReferenceState
                .DeclaredNotHealthChecked
            : UnifiedProviderOpaqueCredentialReferenceState.NotConfigured;
        CredentialReferenceFingerprint = desired.CredentialConfigured
            ? UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                "D033/CURRENT-METADATA/OPAQUE-CREDENTIAL-REFERENCE/1",
                desired.CredentialFile,
                desired.CredentialSha256)
            : UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                "D033/CURRENT-METADATA/OPAQUE-CREDENTIAL-REFERENCE/1",
                "NONE");
    }

    private static int ProtocolKind(int providerKind)
        => providerKind switch
        {
            1 or 3 => 2,
            2 => 3,
            4 or 6 or 9 or 10 or 11 or 12 or 13 => 7,
            5 => 4,
            7 or 8 => 5,
            _ => throw new InvalidDataException(
                "The current Provider protocol identity was unavailable.")
        };
}

/// <summary>
/// Hardened, current-only observer.  It never opens Store fallback, legacy
/// configuration, providers-v1, Secrets, credential, IPC, process, or network
/// paths.  The production entry has no path parameter.
/// </summary>
internal static class UnifiedProviderCurrentMetadataAuthority
{
    internal const string CurrentAuthoritySchemaIdentity =
        "D033/CURRENT-METADATA-AUTHORITY-SCHEMA/1";
    internal static readonly string CurrentAuthoritySchemaFingerprint =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
            CurrentAuthoritySchemaIdentity,
            UnifiedProviderControlPlaneCanonicalSnapshotCodec.CodecSchemaIdentity,
            UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .CodecSchemaFingerprint,
            "CURRENT-ONLY",
            "DESIRED-ONLY",
            "NO-FALLBACK",
            "NO-SECRET",
            "NO-NETWORK");
    internal const string DesiredOwnerIdentity =
        "D033/DESIRED-CONTROL-PLANE-OWNER/1";
    internal static readonly string DesiredOwnerFingerprint =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
            DesiredOwnerIdentity,
            CurrentAuthoritySchemaIdentity,
            CurrentAuthoritySchemaFingerprint,
            "EFFECTIVE-AUTHORITY-UNAVAILABLE");

    private const string PlayerDirectory = "LilithAiMod";
    private const string CurrentRelativeDirectory =
        "Config\\provider-control-v2";
    private const string CurrentManifestFile = "current-v1.json";
    private const string GenerationsDirectory = "generations";
#if D042_CURRENT_METADATA_ISOLATED_TEST_SEAM
    internal const string IsolatedTestSeamMarker =
        "D042_CURRENT_METADATA_ISOLATED_TEST_SEAM";
#endif

    internal static UnifiedProviderCurrentMetadataResult ObserveCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return UnifiedProviderCurrentMetadataResult.Stop(
                UnifiedProviderCurrentMetadataAdmission.UnsupportedPlatform);
        }
#if D042_CURRENT_METADATA_ISOLATED_TEST_SEAM
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return ObserveDerivedRootForIsolatedTestSeam(
            localApplicationData,
            requireExactCurrentUserRoot: true);
#else
        return ObserveFixedCurrentUserLocalApplicationData();
#endif
    }

#if D042_CURRENT_METADATA_ISOLATED_TEST_SEAM
    internal static UnifiedProviderCurrentMetadataResult
        ObserveCurrentForIsolatedTestRoot(
            string isolatedLocalApplicationDataRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            return UnifiedProviderCurrentMetadataResult.Stop(
                UnifiedProviderCurrentMetadataAdmission.UnsupportedPlatform);
        }
        try
        {
            var root = Path.GetFullPath(
                isolatedLocalApplicationDataRoot
                ?? throw new ArgumentNullException(
                    nameof(isolatedLocalApplicationDataRoot)));
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!root.StartsWith(
                    temporaryRoot,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    temporaryRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return UnifiedProviderCurrentMetadataResult.Stop(
                    UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
            }
            return ObserveDerivedRootForIsolatedTestSeam(
                root,
                requireExactCurrentUserRoot: false);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return UnifiedProviderCurrentMetadataResult.Stop(
                UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
        }
    }
#endif

    [SupportedOSPlatform("windows")]
#if D042_CURRENT_METADATA_ISOLATED_TEST_SEAM
    private static UnifiedProviderCurrentMetadataResult
        ObserveDerivedRootForIsolatedTestSeam(
        string localApplicationData,
        bool requireExactCurrentUserRoot)
#else
    private static UnifiedProviderCurrentMetadataResult
        ObserveFixedCurrentUserLocalApplicationData()
#endif
    {
        byte[]? manifestBytes = null;
        byte[]? payloadBytes = null;
        try
        {
#if D042_CURRENT_METADATA_ISOLATED_TEST_SEAM
            var localRoot = ValidateIsolatedTestSeamRoot(
                localApplicationData,
                requireExactCurrentUserRoot);
#else
            var localRoot = ValidateFixedCurrentUserLocalApplicationDataRoot();
#endif
            var playerRoot = Path.Combine(localRoot, PlayerDirectory);
            var configRoot = Path.Combine(playerRoot, "Config");
            var controlRoot = Path.Combine(
                localRoot,
                PlayerDirectory,
                CurrentRelativeDirectory);
            var generationsPath = Path.Combine(
                controlRoot,
                GenerationsDirectory);
            var currentPath = Path.Combine(controlRoot, CurrentManifestFile);
            using var localDirectory =
                WindowsCurrentMetadataFile.OpenDirectory(localRoot);
            using var playerDirectory =
                WindowsCurrentMetadataFile.OpenDirectory(playerRoot);
            using var configDirectory =
                WindowsCurrentMetadataFile.OpenDirectory(configRoot);
            using var controlDirectory =
                WindowsCurrentMetadataFile.OpenDirectory(controlRoot);
            using var generationsDirectory =
                WindowsCurrentMetadataFile.OpenDirectory(generationsPath);
            WindowsCurrentMetadataFile.AssertNoReparseAncestry(
                currentPath,
                includeLeaf: false);
            using var manifestFile = WindowsCurrentMetadataFile.Open(
                currentPath,
                UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .MaximumManifestBytes,
                UnifiedProviderCurrentMetadataAdmission.Missing);
            manifestBytes = manifestFile.CopyBytes();
            var manifest =
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.DecodeManifest(
                    manifestBytes,
                    requireCurrentSchema: true);

            var payloadPath = Path.GetFullPath(Path.Combine(
                controlRoot,
                GenerationsDirectory,
                manifest.PayloadFile));
            var generationsRoot = Path.GetFullPath(generationsPath)
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!payloadPath.StartsWith(
                    generationsRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.Corrupt);
            }
            using var payloadFile = WindowsCurrentMetadataFile.Open(
                payloadPath,
                UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .MaximumPayloadBytes,
                UnifiedProviderCurrentMetadataAdmission.Corrupt);
            payloadBytes = payloadFile.CopyBytes();
            var payload =
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.DecodePayload(
                    payloadBytes,
                    manifest,
                    requireCurrentSchema: true);

            var desired = ResolveDesired(payload);
            manifestFile.AssertUnchanged();
            payloadFile.AssertUnchanged();
            generationsDirectory.AssertUnchanged();
            controlDirectory.AssertUnchanged();
            configDirectory.AssertUnchanged();
            playerDirectory.AssertUnchanged();
            localDirectory.AssertUnchanged();
            WindowsCurrentMetadataFile.AssertNoReparseAncestry(
                currentPath,
                includeLeaf: true);
            WindowsCurrentMetadataFile.AssertNoReparseAncestry(
                payloadPath,
                includeLeaf: true);

            var manifestSha256 =
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.Sha256(
                    manifestBytes);
            var payloadSha256 =
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.Sha256(
                    payloadBytes);
            var authorityFingerprint =
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                    "D033/CURRENT-METADATA-AUTHORITY/1",
                    UnifiedProviderControlPlaneCanonicalSnapshotCodec
                        .CodecIdentity,
                    manifest.Schema.ToString(CultureInfo.InvariantCulture),
                    manifest.StoreGeneration.ToString(CultureInfo.InvariantCulture),
                    manifest.SnapshotFingerprint,
                    manifestSha256,
                    payloadSha256,
                    localDirectory.ObjectFingerprint,
                    playerDirectory.ObjectFingerprint,
                    configDirectory.ObjectFingerprint,
                    controlDirectory.ObjectFingerprint,
                    generationsDirectory.ObjectFingerprint,
                    manifestFile.ObjectFingerprint,
                    payloadFile.ObjectFingerprint);
            return UnifiedProviderCurrentMetadataResult.Accept(
                new UnifiedProviderCurrentMetadataProjection(
                    manifest.StoreGeneration,
                    manifest.SnapshotFingerprint,
                    manifestSha256,
                    payloadSha256,
                    manifest.CreatedAtUnixMilliseconds,
                    authorityFingerprint,
                    desired));
        }
        catch (CurrentMetadataReadException exception)
        {
            return UnifiedProviderCurrentMetadataResult.Stop(exception.Admission);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or System.Text.Json.JsonException
            or FormatException
            or OverflowException)
        {
            return UnifiedProviderCurrentMetadataResult.Stop(
                UnifiedProviderCurrentMetadataAdmission.Corrupt);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or Win32Exception
            or CryptographicException)
        {
            return UnifiedProviderCurrentMetadataResult.Stop(
                UnifiedProviderCurrentMetadataAdmission.StorageFailure);
        }
        finally
        {
            if (manifestBytes != null)
                CryptographicOperations.ZeroMemory(manifestBytes);
            if (payloadBytes != null)
                CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    private static UnifiedProviderControlPlaneCanonicalInstance? ResolveDesired(
        UnifiedProviderControlPlaneCanonicalPayload payload)
    {
        var assignment = payload.TextAssignment;
        if (assignment == null)
            return null;
        var instances = payload.Instances
            ?? throw new InvalidDataException(
                "The current Provider instances were missing.");
        return instances.Single(value =>
            string.Equals(
                value.InstanceId,
                assignment.InstanceId,
                StringComparison.Ordinal)
            && string.Equals(
                value.SelectedModel,
                assignment.ModelId,
                StringComparison.Ordinal)
            && value.ConfigurationGeneration
                == assignment.ConfigurationGeneration
            && string.Equals(
                UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .ComputeCurrentConfigurationFingerprint(value),
                assignment.ConfigurationFingerprint,
                StringComparison.Ordinal));
    }

    [SupportedOSPlatform("windows")]
#if D042_CURRENT_METADATA_ISOLATED_TEST_SEAM
    private static string ValidateIsolatedTestSeamRoot(
        string value,
        bool requireExactCurrentUserRoot)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Path.IsPathFullyQualified(value)
            || value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith(@"\\?\", StringComparison.Ordinal)
            || value.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new CurrentMetadataReadException(
                UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
        }
        var full = Path.GetFullPath(value)
            .TrimEnd(Path.DirectorySeparatorChar);
        if (requireExactCurrentUserRoot)
        {
            var expected = Path.GetFullPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(full, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
            }
        }
        var volume = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(volume)
            || new DriveInfo(volume).DriveType != DriveType.Fixed)
        {
            throw new CurrentMetadataReadException(
                UnifiedProviderCurrentMetadataAdmission.NonFixedLocalVolume);
        }
        return full;
    }
#else
    private static string ValidateFixedCurrentUserLocalApplicationDataRoot()
    {
        var value = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(value)
            || !Path.IsPathFullyQualified(value)
            || value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith(@"\\?\", StringComparison.Ordinal)
            || value.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new CurrentMetadataReadException(
                UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
        }
        var full = Path.GetFullPath(value)
            .TrimEnd(Path.DirectorySeparatorChar);
        var expected = Path.GetFullPath(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData))
            .TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(full, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new CurrentMetadataReadException(
                UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
        }
        var volume = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(volume)
            || new DriveInfo(volume).DriveType != DriveType.Fixed)
        {
            throw new CurrentMetadataReadException(
                UnifiedProviderCurrentMetadataAdmission.NonFixedLocalVolume);
        }
        return full;
    }
#endif

    private sealed class CurrentMetadataReadException : IOException
    {
        internal UnifiedProviderCurrentMetadataAdmission Admission { get; }

        internal CurrentMetadataReadException(
            UnifiedProviderCurrentMetadataAdmission admission,
            Exception? inner = null)
            : base("The current Provider metadata authority stopped.", inner)
        {
            Admission = admission;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsCurrentMetadataFile : IDisposable
    {
        private const uint GenericRead = 0x80000000;
        private const uint ReadControl = 0x00020000;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagSequentialScan = 0x08000000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const int FileIdInfoClass = 18;
        private const int ErrorFileNotFound = 2;
        private const int ErrorPathNotFound = 3;
        private const int ErrorHandleEof = 38;
        private const uint OwnerSecurityInformation = 0x00000001;
        private const uint GroupSecurityInformation = 0x00000002;
        private const uint DaclSecurityInformation = 0x00000004;
        private const int SeFileObject = 1;
        private static readonly IntPtr InvalidFindHandle = new(-1);

        private readonly string _path;
        private readonly int _maximumBytes;
        private readonly FileStream _stream;
        private readonly NativeFileIdentity _identity;
        private readonly string _securitySha256;
        private readonly string _streamsIdentity;
        private readonly byte[] _bytes;
        private int _disposed;

        internal string ObjectFingerprint { get; }

        private WindowsCurrentMetadataFile(
            string path,
            int maximumBytes,
            FileStream stream,
            NativeFileIdentity identity,
            string securitySha256,
            string streamsIdentity,
            byte[] bytes)
        {
            _path = path;
            _maximumBytes = maximumBytes;
            _stream = stream;
            _identity = identity;
            _securitySha256 = securitySha256;
            _streamsIdentity = streamsIdentity;
            _bytes = bytes;
            ObjectFingerprint =
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                    "D033/CURRENT-METADATA-FILE-OBJECT/1",
                    identity.VolumeIdentity,
                    identity.FileId128,
                    identity.Length.ToString(CultureInfo.InvariantCulture),
                    identity.Attributes.ToString(CultureInfo.InvariantCulture),
                    securitySha256,
                    streamsIdentity,
                UnifiedProviderControlPlaneCanonicalSnapshotCodec.Sha256(bytes));
        }

        internal static WindowsCurrentMetadataDirectory OpenDirectory(
            string exactPath)
        {
            ValidateExactPath(exactPath);
            AssertNoReparseAncestry(exactPath, includeLeaf: true);
            var handle = CreateFile(
                exactPath,
                FileReadAttributes | ReadControl,
                (uint)FileShare.Read,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error is ErrorFileNotFound or ErrorPathNotFound)
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.Missing);
                }
                throw new Win32Exception(error);
            }
            try
            {
                var identity = ReadIdentity(handle);
                ValidateDirectoryIdentity(exactPath, handle, identity);
                var security = ReadSecuritySha256(handle);
                var streams = ReadDirectoryStreamsIdentity(exactPath);
                return new WindowsCurrentMetadataDirectory(
                    exactPath,
                    handle,
                    identity,
                    security,
                    streams);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal static WindowsCurrentMetadataFile Open(
            string exactPath,
            int maximumBytes,
            UnifiedProviderCurrentMetadataAdmission missingAdmission)
        {
            ValidateExactPath(exactPath);
            AssertNoReparseAncestry(exactPath, includeLeaf: false);
            var handle = CreateFile(
                exactPath,
                GenericRead | ReadControl,
                (uint)FileShare.Read,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal
                | FileFlagOpenReparsePoint
                | FileFlagSequentialScan,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error is ErrorFileNotFound or ErrorPathNotFound)
                    throw new CurrentMetadataReadException(missingAdmission);
                throw new Win32Exception(error);
            }

            FileStream? stream = null;
            try
            {
                stream = new FileStream(
                    handle,
                    FileAccess.Read,
                    64 * 1024,
                    isAsync: false);
                handle = null!;
                var before = ReadIdentity(stream.SafeFileHandle);
                ValidateIdentity(exactPath, stream.SafeFileHandle, before);
                var securityBefore = ReadSecuritySha256(stream.SafeFileHandle);
                var streamsBefore = ReadStreamsIdentity(exactPath, before.Length);
                var first = ReadBounded(stream, maximumBytes);
                var second = ReadBounded(stream, maximumBytes);
                var after = ReadIdentity(stream.SafeFileHandle);
                var securityAfter = ReadSecuritySha256(stream.SafeFileHandle);
                var streamsAfter = ReadStreamsIdentity(exactPath, after.Length);
                if (!before.Equals(after)
                    || !first.AsSpan().SequenceEqual(second)
                    || !string.Equals(
                        securityBefore,
                        securityAfter,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        streamsBefore,
                        streamsAfter,
                        StringComparison.Ordinal))
                {
                    CryptographicOperations.ZeroMemory(first);
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.ChangedDuringRead);
                }
                CryptographicOperations.ZeroMemory(second);
                return new WindowsCurrentMetadataFile(
                    exactPath,
                    maximumBytes,
                    stream,
                    before,
                    securityBefore,
                    streamsBefore,
                    first);
            }
            catch
            {
                stream?.Dispose();
                if (stream == null)
                    handle.Dispose();
                throw;
            }
        }

        internal byte[] CopyBytes()
        {
            ThrowIfDisposed();
            return _bytes.ToArray();
        }

        internal void AssertUnchanged()
        {
            ThrowIfDisposed();
            var identity = ReadIdentity(_stream.SafeFileHandle);
            ValidateIdentity(_path, _stream.SafeFileHandle, identity);
            var security = ReadSecuritySha256(_stream.SafeFileHandle);
            var streams = ReadStreamsIdentity(_path, identity.Length);
            var bytes = ReadBounded(_stream, _maximumBytes);
            try
            {
                if (!_identity.Equals(identity)
                    || !string.Equals(
                        _securitySha256,
                        security,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        _streamsIdentity,
                        streams,
                        StringComparison.Ordinal)
                    || !_bytes.AsSpan().SequenceEqual(bytes))
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.ChangedDuringRead);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            CryptographicOperations.ZeroMemory(_bytes);
            _stream.Dispose();
        }

        internal static void AssertNoReparseAncestry(
            string exactPath,
            bool includeLeaf)
        {
            ValidateExactPath(exactPath);
            var current = includeLeaf
                ? new FileInfo(exactPath).FullName
                : new FileInfo(exactPath).Directory?.FullName;
            while (!string.IsNullOrEmpty(current))
            {
                var attributes = GetFileAttributes(current);
                if (attributes == uint.MaxValue)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error is ErrorFileNotFound or ErrorPathNotFound)
                    {
                        if (!includeLeaf
                            && string.Equals(
                                current,
                                new FileInfo(exactPath).Directory?.FullName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        throw new CurrentMetadataReadException(
                            UnifiedProviderCurrentMetadataAdmission.Missing);
                    }
                    throw new Win32Exception(error);
                }
                if (((FileAttributes)attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.ReparseRejected);
                }
                var parent = Directory.GetParent(current);
                if (parent == null)
                    break;
                current = parent.FullName;
            }
        }

        private static byte[] ReadBounded(FileStream stream, int maximumBytes)
        {
            var length = stream.Length;
            if (length <= 0 || length > maximumBytes || length > int.MaxValue)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.OutOfBounds);
            }
            var bytes = new byte[checked((int)length)];
            stream.Position = 0;
            var offset = 0;
            while (offset < bytes.Length)
            {
                var count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0)
                    throw new EndOfStreamException();
                offset += count;
            }
            if (stream.ReadByte() != -1)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.ChangedDuringRead);
            }
            return bytes;
        }

        private static void ValidateIdentity(
            string exactPath,
            SafeFileHandle handle,
            NativeFileIdentity identity)
        {
            if ((identity.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.ReparseRejected);
            }
            if ((identity.Attributes & (uint)FileAttributes.Directory) != 0
                || identity.FileId128.Length != 32
                || identity.VolumeIdentity.Length != 16)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.FileIdentityRejected);
            }
            if (identity.HardLinkCount != 1)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.HardLinkRejected);
            }
            var finalPath = ReadFinalPath(handle);
            if (!string.Equals(
                    finalPath,
                    exactPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.FinalPathMismatch);
            }
        }

        private static void ValidateDirectoryIdentity(
            string exactPath,
            SafeFileHandle handle,
            NativeFileIdentity identity)
        {
            if ((identity.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.ReparseRejected);
            }
            if ((identity.Attributes & (uint)FileAttributes.Directory) == 0
                || identity.FileId128.Length != 32
                || identity.VolumeIdentity.Length != 16)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.FileIdentityRejected);
            }
            var finalPath = ReadFinalPath(handle);
            if (!string.Equals(
                    finalPath.TrimEnd(Path.DirectorySeparatorChar),
                    exactPath.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.FinalPathMismatch);
            }
        }

        private static NativeFileIdentity ReadIdentity(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileIdInfoClass,
                    out var fileId,
                    Marshal.SizeOf<FileIdInfo>())
                || !GetFileInformationByHandle(handle, out var legacy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var fileIdBytes = BitConverter.GetBytes(fileId.FileIdLow)
                .Concat(BitConverter.GetBytes(fileId.FileIdHigh))
                .ToArray();
            return new NativeFileIdentity(
                fileId.VolumeSerialNumber.ToString("X16", CultureInfo.InvariantCulture),
                Convert.ToHexString(fileIdBytes),
                legacy.NumberOfLinks,
                legacy.FileSizeHigh == 0
                    ? legacy.FileSizeLow
                    : checked(((long)legacy.FileSizeHigh << 32)
                              | legacy.FileSizeLow),
                legacy.FileAttributes);
        }

        private static string ReadFinalPath(SafeFileHandle handle)
        {
            var builder = new StringBuilder(512);
            var length = GetFinalPathNameByHandle(
                handle,
                builder,
                (uint)builder.Capacity,
                0);
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length >= builder.Capacity)
            {
                builder = new StringBuilder(checked((int)length + 1));
                length = GetFinalPathNameByHandle(
                    handle,
                    builder,
                    (uint)builder.Capacity,
                    0);
                if (length == 0 || length >= builder.Capacity)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var value = builder.ToString();
            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.NonFixedLocalVolume);
            }
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
                value = value[4..];
            return Path.GetFullPath(value);
        }

        private static string ReadSecuritySha256(SafeFileHandle handle)
        {
            var information = OwnerSecurityInformation
                | GroupSecurityInformation
                | DaclSecurityInformation;
            var result = GetSecurityInfo(
                handle.DangerousGetHandle(),
                SeFileObject,
                information,
                out _,
                out _,
                out _,
                out _,
                out var descriptor);
            if (result != 0 || descriptor == IntPtr.Zero)
                throw new Win32Exception(unchecked((int)result));
            try
            {
                var length = GetSecurityDescriptorLength(descriptor);
                if (length == 0 || length > 64 * 1024)
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.SecurityRejected);
                }
                var bytes = new byte[checked((int)length)];
                Marshal.Copy(descriptor, bytes, 0, bytes.Length);
                try
                {
                    ValidateCurrentUserDacl(bytes);
                    return Convert.ToHexString(SHA256.HashData(bytes));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                _ = LocalFree(descriptor);
            }
        }

        private static void ValidateCurrentUserDacl(byte[] descriptorBytes)
        {
            var descriptor = new RawSecurityDescriptor(descriptorBytes, 0);
            using var identity = WindowsIdentity.GetCurrent();
            var currentUser = identity.User?.Value;
            if (string.IsNullOrEmpty(currentUser)
                || !string.Equals(
                    descriptor.Owner?.Value,
                    currentUser,
                    StringComparison.Ordinal)
                || (descriptor.ControlFlags
                    & ControlFlags.DiscretionaryAclPresent) == 0
                || descriptor.DiscretionaryAcl == null)
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.SecurityRejected);
            }
            var allowedTrustees = new[]
            {
                currentUser,
                "S-1-5-18",
                "S-1-5-32-544"
            };
            foreach (GenericAce ace in descriptor.DiscretionaryAcl)
            {
                if (ace is not QualifiedAce qualified
                    || qualified.AceQualifier != AceQualifier.AccessAllowed)
                {
                    continue;
                }
                if (qualified is not CommonAce common
                    || common.IsCallback
                    || common.SecurityIdentifier == null
                    || !allowedTrustees.Contains(
                        common.SecurityIdentifier.Value,
                        StringComparer.Ordinal))
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.SecurityRejected);
                }
            }
        }

        private static string ReadStreamsIdentity(string path, long expectedLength)
        {
            var handle = FindFirstStream(path, 0, out var data, 0);
            if (handle == InvalidFindHandle)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var count = 0;
                var identity = string.Empty;
                while (true)
                {
                    count++;
                    if (count != 1
                        || !string.Equals(
                            data.StreamName,
                            "::$DATA",
                            StringComparison.Ordinal)
                        || data.StreamSize != expectedLength)
                    {
                        throw new CurrentMetadataReadException(
                            UnifiedProviderCurrentMetadataAdmission
                                .NamedStreamRejected);
                    }
                    identity = UnifiedProviderControlPlaneCanonicalSnapshotCodec
                        .Hash(
                            "D033/CURRENT-METADATA-STREAMS/1",
                            data.StreamName,
                            data.StreamSize.ToString(CultureInfo.InvariantCulture));
                    if (!FindNextStream(handle, out data))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorHandleEof)
                            throw new Win32Exception(error);
                        break;
                    }
                }
                return identity;
            }
            finally
            {
                _ = FindClose(handle);
            }
        }

        private static string ReadDirectoryStreamsIdentity(string path)
        {
            var handle = FindFirstStream(path, 0, out var data, 0);
            if (handle == InvalidFindHandle)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorHandleEof)
                {
                    return UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                        "D033/CURRENT-METADATA-DIRECTORY-STREAMS/1",
                        "NONE");
                }
                throw new Win32Exception(error);
            }
            try
            {
                if (!string.Equals(
                        data.StreamName,
                        "::$DATA",
                        StringComparison.Ordinal)
                    || data.StreamSize != 0)
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.NamedStreamRejected);
                }
                if (FindNextStream(handle, out _))
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.NamedStreamRejected);
                }
                var nextError = Marshal.GetLastWin32Error();
                if (nextError != ErrorHandleEof)
                    throw new Win32Exception(nextError);
                return UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                    "D033/CURRENT-METADATA-DIRECTORY-STREAMS/1",
                    data.StreamName,
                    data.StreamSize.ToString(CultureInfo.InvariantCulture));
            }
            finally
            {
                _ = FindClose(handle);
            }
        }

        private static void ValidateExactPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || !Path.IsPathFullyQualified(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal)
                || path.StartsWith(@"\\?\", StringComparison.Ordinal)
                || path.StartsWith(@"\\.\", StringComparison.Ordinal)
                || path.Contains(Path.AltDirectorySeparatorChar)
                || !string.Equals(
                    Path.GetFullPath(path),
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CurrentMetadataReadException(
                    UnifiedProviderCurrentMetadataAdmission.InvalidRoot);
            }
        }

        private void ThrowIfDisposed()
        {
            if (System.Threading.Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(WindowsCurrentMetadataFile));
        }

        internal sealed class WindowsCurrentMetadataDirectory : IDisposable
        {
            private readonly string _path;
            private readonly SafeFileHandle _handle;
            private readonly NativeFileIdentity _identity;
            private readonly string _securitySha256;
            private readonly string _streamsIdentity;
            private int _disposed;

            internal string ObjectFingerprint { get; }

            internal WindowsCurrentMetadataDirectory(
                string path,
                SafeFileHandle handle,
                NativeFileIdentity identity,
                string securitySha256,
                string streamsIdentity)
            {
                _path = path;
                _handle = handle;
                _identity = identity;
                _securitySha256 = securitySha256;
                _streamsIdentity = streamsIdentity;
                ObjectFingerprint =
                    UnifiedProviderControlPlaneCanonicalSnapshotCodec.Hash(
                        "D033/CURRENT-METADATA-DIRECTORY-OBJECT/1",
                        identity.VolumeIdentity,
                        identity.FileId128,
                        identity.Attributes.ToString(CultureInfo.InvariantCulture),
                        securitySha256,
                        streamsIdentity);
            }

            internal void AssertUnchanged()
            {
                ThrowIfDisposed();
                var identity = ReadIdentity(_handle);
                ValidateDirectoryIdentity(_path, _handle, identity);
                var security = ReadSecuritySha256(_handle);
                var streams = ReadDirectoryStreamsIdentity(_path);
                if (!_identity.Equals(identity)
                    || !string.Equals(
                        _securitySha256,
                        security,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        _streamsIdentity,
                        streams,
                        StringComparison.Ordinal))
                {
                    throw new CurrentMetadataReadException(
                        UnifiedProviderCurrentMetadataAdmission.ChangedDuringRead);
                }
            }

            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                _handle.Dispose();
            }

            private void ThrowIfDisposed()
            {
                if (System.Threading.Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(
                        nameof(WindowsCurrentMetadataDirectory));
                }
            }
        }

        internal readonly struct NativeFileIdentity : IEquatable<NativeFileIdentity>
        {
            internal string VolumeIdentity { get; }
            internal string FileId128 { get; }
            internal uint HardLinkCount { get; }
            internal long Length { get; }
            internal uint Attributes { get; }

            internal NativeFileIdentity(
                string volumeIdentity,
                string fileId128,
                uint hardLinkCount,
                long length,
                uint attributes)
            {
                VolumeIdentity = volumeIdentity;
                FileId128 = fileId128;
                HardLinkCount = hardLinkCount;
                Length = length;
                Attributes = attributes;
            }

            public bool Equals(NativeFileIdentity other)
                => string.Equals(
                       VolumeIdentity,
                       other.VolumeIdentity,
                       StringComparison.Ordinal)
                   && string.Equals(
                       FileId128,
                       other.FileId128,
                       StringComparison.Ordinal)
                   && HardLinkCount == other.HardLinkCount
                   && Length == other.Length
                   && Attributes == other.Attributes;

            public override bool Equals(object? obj)
                => obj is NativeFileIdentity other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    VolumeIdentity,
                    FileId128,
                    HardLinkCount,
                    Length,
                    Attributes);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfo
        {
            public ulong VolumeSerialNumber;
            public ulong FileIdLow;
            public ulong FileIdHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public NativeFileTime CreationTime;
            public NativeFileTime LastAccessTime;
            public NativeFileTime LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Win32FindStreamData
        {
            public long StreamSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
            public string StreamName;
        }

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint access,
            uint share,
            IntPtr security,
            uint creation,
            uint flags,
            IntPtr template);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetFinalPathNameByHandleW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle handle,
            StringBuilder path,
            uint pathLength,
            uint flags);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetFileAttributesW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint GetFileAttributes(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle handle,
            int informationClass,
            out FileIdInfo information,
            int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle handle,
            out ByHandleFileInformation information);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "FindFirstStreamW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern IntPtr FindFirstStream(
            string fileName,
            int infoLevel,
            out Win32FindStreamData data,
            uint flags);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "FindNextStreamW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextStream(
            IntPtr handle,
            out Win32FindStreamData data);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetSecurityInfo(
            IntPtr handle,
            int objectType,
            uint information,
            out IntPtr owner,
            out IntPtr group,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr descriptor);

        [DllImport("advapi32.dll")]
        private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
