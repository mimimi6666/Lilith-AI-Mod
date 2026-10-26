using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LilithAgentWorkbench;

internal enum D045AuthorityCapability
{
    SanitizedLilithStatusRead = 1,
    PublicHttpsUrlFetch = 2,
    ClipboardRead = 3,
    LocalFileRead = 4,
    LocalFolderEnumerate = 5,
    ScreenshotCapture = 6,
    ScreenshotUpload = 7,
    LocalContentExternalShare = 8,
    GracefulProcessClose = 9,
    ForceTerminateProcess = 10,
    RecycleBinDelete = 11,
    PermanentDelete = 12,
    Shutdown = 13,
    Restart = 14,
    RawShellArbitraryCommand = 15,
    ArbitraryExternalAppInputKeystrokes = 16,
    GeneralElevationAdminAccess = 17,
    LocalDataCreate = 18,
    ApplicationLaunch = 19,
    LocalContentOpen = 20,
    ClipboardWrite = 21,
    SystemDiagnosticsRead = 22,
    ApplicationInventoryRead = 23,
    ProcessInventoryRead = 24,
    WindowInventoryRead = 25,
    ExternalAppSemanticUiInspect = 26,
    LocalPathSearch = 27,
    LocalContentSearch = 28,
    FileModify = 29,
    LocalContentCopy = 30,
    LocalContentMoveRename = 31,
    FolderTreeCreate = 32,
    ArchiveManage = 33,
    WindowControl = 34,
    ExternalAppSemanticUiAction = 35,
    ExternalAppPointerInput = 36,
    ApplicationLaunchWithArguments = 37,
    ProcessExecuteNonInteractive = 38,
    ProcessBackgroundManage = 39,
    WebPublicSearch = 40,
    BrowserNavigate = 41,
    DownloadPublicHttps = 42
}

internal enum D045AuthorityDisposition
{
    NotImplementedEffectiveOff = 1,
    UnavailableConstitutionAmendmentRequiredEffectiveOff = 2,
    PlayerControlledHarnessDefaultOff = 3
}

internal enum D045AuthorityCatalogValidationFailure
{
    None = 0,
    MissingRow = 1,
    ExtraRow = 2,
    NullClaim = 3,
    DuplicateRow = 4,
    ReorderedRow = 5,
    UnknownRow = 6,
    CaseSubstitution = 7,
    OrdinalSubstitution = 8,
    CapabilitySubstitution = 9,
    StableIdSubstitution = 10,
    DisplayLabelSubstitution = 11,
    DispositionSubstitution = 12
}

internal sealed class D045AuthorityCatalogRow
{
    internal int Ordinal { get; }
    internal D045AuthorityCapability Capability { get; }
    internal string StableId { get; }
    internal string DisplayLabel { get; }
    internal D045AuthorityDisposition Disposition { get; }
    internal bool Available =>
        Disposition == D045AuthorityDisposition.PlayerControlledHarnessDefaultOff;
    internal bool Effective => false;
    internal bool ConstitutionAmendmentRequired =>
        Disposition
        == D045AuthorityDisposition
            .UnavailableConstitutionAmendmentRequiredEffectiveOff;

    private D045AuthorityCatalogRow(
        int ordinal,
        D045AuthorityCapability capability,
        string stableId,
        string displayLabel,
        D045AuthorityDisposition disposition)
    {
        Ordinal = ordinal;
        Capability = capability;
        StableId = stableId;
        DisplayLabel = displayLabel;
        Disposition = disposition;
    }

    internal D045AuthorityCatalogClaim Observe()
        => new(
            Ordinal,
            Capability,
            StableId,
            DisplayLabel,
            Disposition);

    internal static D045AuthorityCatalogRow CreateCanonical(
        D045AuthorityCapability capability)
        => capability switch
        {
            D045AuthorityCapability.SanitizedLilithStatusRead =>
                Create(
                    capability,
                    "d045.status.sanitized-read",
                    "Sanitized Lilith status read"),
            D045AuthorityCapability.PublicHttpsUrlFetch =>
                Create(
                    capability,
                    "d045.url.public-https-fetch",
                    "Public HTTPS URL fetch"),
            D045AuthorityCapability.ClipboardRead =>
                Create(
                    capability,
                    "d045.clipboard.read",
                    "Clipboard read"),
            D045AuthorityCapability.LocalFileRead =>
                Create(
                    capability,
                    "d045.file.read",
                    "Local file read"),
            D045AuthorityCapability.LocalFolderEnumerate =>
                Create(
                    capability,
                    "d045.folder.enumerate",
                    "Local folder enumerate"),
            D045AuthorityCapability.ScreenshotCapture =>
                Create(
                    capability,
                    "d045.screenshot.capture",
                    "Screenshot capture"),
            D045AuthorityCapability.ScreenshotUpload =>
                Create(
                    capability,
                    "d045.screenshot.upload",
                    "Screenshot upload"),
            D045AuthorityCapability.LocalContentExternalShare =>
                Create(
                    capability,
                    "d045.local-content.external-share",
                    "Local-content external share"),
            D045AuthorityCapability.GracefulProcessClose =>
                Create(
                    capability,
                    "d045.process.graceful-close",
                    "Graceful process close"),
            D045AuthorityCapability.ForceTerminateProcess =>
                Create(
                    capability,
                    "d045.process.force-terminate",
                    "Force terminate process"),
            D045AuthorityCapability.RecycleBinDelete =>
                Create(
                    capability,
                    "d045.delete.recycle-bin",
                    "Recycle-bin delete"),
            D045AuthorityCapability.PermanentDelete =>
                Create(
                    capability,
                    "d045.delete.permanent",
                    "Permanent delete"),
            D045AuthorityCapability.Shutdown =>
                Create(
                    capability,
                    "d045.power.shutdown",
                    "Shutdown"),
            D045AuthorityCapability.Restart =>
                Create(
                    capability,
                    "d045.power.restart",
                    "Restart"),
            D045AuthorityCapability.RawShellArbitraryCommand =>
                Create(
                    capability,
                    "d045.shell.raw-arbitrary-command",
                    "Raw Shell/arbitrary command"),
            D045AuthorityCapability.ArbitraryExternalAppInputKeystrokes =>
                Create(
                    capability,
                    "d045.external-app.arbitrary-input",
                    "Arbitrary external-app input/keystrokes"),
            D045AuthorityCapability.GeneralElevationAdminAccess =>
                Create(
                    capability,
                    "d045.privilege.general-elevation",
                    "General elevation/admin access"),
            D045AuthorityCapability.LocalDataCreate =>
                Create(
                    capability,
                    "d045.data.create",
                    "Local data create"),
            D045AuthorityCapability.ApplicationLaunch =>
                Create(
                    capability,
                    "d045.application.launch",
                    "Application launch"),
            D045AuthorityCapability.LocalContentOpen =>
                Create(
                    capability,
                    "d045.local-content.open",
                    "開啟本機內容"),
            D045AuthorityCapability.ClipboardWrite =>
                Create(capability, "d045.clipboard.write", "Clipboard write"),
            D045AuthorityCapability.SystemDiagnosticsRead =>
                Create(capability, "d045.system.diagnostics-read", "System diagnostics read"),
            D045AuthorityCapability.ApplicationInventoryRead =>
                Create(capability, "d045.application.inventory-read", "Application inventory read"),
            D045AuthorityCapability.ProcessInventoryRead =>
                Create(capability, "d045.process.inventory-read", "Process inventory read"),
            D045AuthorityCapability.WindowInventoryRead =>
                Create(capability, "d045.window.inventory-read", "Window inventory read"),
            D045AuthorityCapability.ExternalAppSemanticUiInspect =>
                Create(capability, "d045.external-app.semantic-ui-inspect", "External-app semantic UI inspect"),
            D045AuthorityCapability.LocalPathSearch =>
                Create(capability, "d045.local-path.search", "Local path search"),
            D045AuthorityCapability.LocalContentSearch =>
                Create(capability, "d045.local-content.search", "Local content search"),
            D045AuthorityCapability.FileModify =>
                Create(capability, "d045.file.modify", "File modify"),
            D045AuthorityCapability.LocalContentCopy =>
                Create(capability, "d045.local-content.copy", "Local-content copy"),
            D045AuthorityCapability.LocalContentMoveRename =>
                Create(capability, "d045.local-content.move-rename", "Local-content move/rename"),
            D045AuthorityCapability.FolderTreeCreate =>
                Create(capability, "d045.folder.tree-create", "Folder tree create"),
            D045AuthorityCapability.ArchiveManage =>
                Create(capability, "d045.archive.manage", "Archive manage"),
            D045AuthorityCapability.WindowControl =>
                Create(capability, "d045.window.control", "Window control"),
            D045AuthorityCapability.ExternalAppSemanticUiAction =>
                Create(capability, "d045.external-app.semantic-ui-action", "External-app semantic UI action"),
            D045AuthorityCapability.ExternalAppPointerInput =>
                Create(capability, "d045.external-app.pointer-input", "External-app pointer input"),
            D045AuthorityCapability.ApplicationLaunchWithArguments =>
                Create(capability, "d045.application.launch-with-arguments", "Application launch with arguments"),
            D045AuthorityCapability.ProcessExecuteNonInteractive =>
                Create(capability, "d045.process.execute-noninteractive", "Non-interactive process execute"),
            D045AuthorityCapability.ProcessBackgroundManage =>
                Create(capability, "d045.process.background-manage", "Background process manage"),
            D045AuthorityCapability.WebPublicSearch =>
                Create(capability, "d045.web.public-search", "Public web search"),
            D045AuthorityCapability.BrowserNavigate =>
                Create(capability, "d045.browser.navigate", "Browser navigate"),
            D045AuthorityCapability.DownloadPublicHttps =>
                Create(capability, "d045.download.public-https", "Public HTTPS download"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(capability),
                capability,
                "Unknown D-045 authority capability.")
        };

    private static D045AuthorityCatalogRow Create(
        D045AuthorityCapability capability,
        string stableId,
        string displayLabel)
        => new(
            (int)capability,
            capability,
            stableId,
            displayLabel,
            D045AuthorityDisposition.PlayerControlledHarnessDefaultOff);

    public override string ToString()
        => $"D045AuthorityCatalogRow({Ordinal}:{StableId}; EffectiveOff)";
}

internal sealed class D045AuthorityCatalogClaim
{
    internal int Ordinal { get; }
    internal D045AuthorityCapability Capability { get; }
    internal string? StableId { get; }
    internal string? DisplayLabel { get; }
    internal D045AuthorityDisposition Disposition { get; }

    internal D045AuthorityCatalogClaim(
        int ordinal,
        D045AuthorityCapability capability,
        string? stableId,
        string? displayLabel,
        D045AuthorityDisposition disposition)
    {
        Ordinal = ordinal;
        Capability = capability;
        StableId = stableId;
        DisplayLabel = displayLabel;
        Disposition = disposition;
    }

    internal static D045AuthorityCatalogClaim FromCanonical(
        D045AuthorityCatalogRow row)
    {
        if (row is null)
            throw new ArgumentNullException(nameof(row));
        return row.Observe();
    }
}

internal sealed class D045AuthorityCatalogValidationResult
{
    internal bool Accepted =>
        Failure == D045AuthorityCatalogValidationFailure.None;
    internal D045AuthorityCatalogValidationFailure Failure { get; }
    internal int FailureIndex { get; }
    internal D045Sha256Fingerprint CanonicalFingerprint { get; }

    private D045AuthorityCatalogValidationResult(
        D045AuthorityCatalogValidationFailure failure,
        int failureIndex,
        D045Sha256Fingerprint canonicalFingerprint)
    {
        Failure = failure;
        FailureIndex = failureIndex;
        CanonicalFingerprint = canonicalFingerprint;
    }

    internal static D045AuthorityCatalogValidationResult Accept(
        D045Sha256Fingerprint fingerprint)
        => new(D045AuthorityCatalogValidationFailure.None, -1, fingerprint);

    internal static D045AuthorityCatalogValidationResult Reject(
        D045AuthorityCatalogValidationFailure failure,
        int failureIndex,
        D045Sha256Fingerprint fingerprint)
        => new(failure, failureIndex, fingerprint);
}

internal static class D045AuthorityTaxonomy
{
    internal const int ExactRowCount = 42;
    internal const int MaximumStableIdAsciiBytes = 64;
    internal const int MaximumDisplayLabelTextElements = 64;
    internal const int MaximumDisplayLabelUtf8Bytes = 192;

    internal static ReadOnlyCollection<D045AuthorityCatalogRow> Rows
        => Array.AsReadOnly(CreateRows());

    internal static D045Sha256Fingerprint CatalogFingerprint
        => ComputeFingerprint(CreateRows());

    internal static bool IsDefined(D045AuthorityCapability capability)
        => (int)capability is >= 1 and <= ExactRowCount;

    internal static bool TryGetRow(
        D045AuthorityCapability capability,
        out D045AuthorityCatalogRow? row)
    {
        if (!IsDefined(capability))
        {
            row = null;
            return false;
        }

        row = D045AuthorityCatalogRow.CreateCanonical(capability);
        return true;
    }

    internal static bool TryResolveExactStableId(
        string? stableId,
        out D045AuthorityCapability capability)
    {
        if (stableId is not null)
        {
            var rows = CreateRows();
            for (var index = 0; index < rows.Length; index++)
            {
                if (string.Equals(
                        stableId,
                        rows[index].StableId,
                        StringComparison.Ordinal))
                {
                    capability = rows[index].Capability;
                    return true;
                }
            }
        }

        capability = default;
        return false;
    }

    internal static D045AuthorityCatalogValidationResult ValidateExactClaims(
        IReadOnlyList<D045AuthorityCatalogClaim?>? claims)
    {
        var rows = CreateRows();
        var fingerprint = ComputeFingerprint(rows);
        if (claims is null || claims.Count < ExactRowCount)
        {
            return D045AuthorityCatalogValidationResult.Reject(
                D045AuthorityCatalogValidationFailure.MissingRow,
                claims?.Count ?? 0,
                fingerprint);
        }

        if (claims.Count > ExactRowCount)
        {
            return D045AuthorityCatalogValidationResult.Reject(
                D045AuthorityCatalogValidationFailure.ExtraRow,
                ExactRowCount,
                fingerprint);
        }

        var seenCapabilities = new HashSet<D045AuthorityCapability>();
        var seenStableIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < claims.Count; index++)
        {
            var claim = claims[index];
            if (claim is null)
            {
                return D045AuthorityCatalogValidationResult.Reject(
                    D045AuthorityCatalogValidationFailure.NullClaim,
                    index,
                    fingerprint);
            }

            if (!seenCapabilities.Add(claim.Capability)
                || (claim.StableId is not null
                    && !seenStableIds.Add(claim.StableId)))
            {
                return D045AuthorityCatalogValidationResult.Reject(
                    D045AuthorityCatalogValidationFailure.DuplicateRow,
                    index,
                    fingerprint);
            }
        }

        for (var index = 0; index < claims.Count; index++)
        {
            var claim = claims[index]!;
            var expected = rows[index];
            var mismatch = ClassifyMismatch(claim, expected, rows);
            if (mismatch != D045AuthorityCatalogValidationFailure.None)
            {
                return D045AuthorityCatalogValidationResult.Reject(
                    mismatch,
                    index,
                    fingerprint);
            }
        }

        return D045AuthorityCatalogValidationResult.Accept(fingerprint);
    }

    private static D045AuthorityCatalogValidationFailure ClassifyMismatch(
        D045AuthorityCatalogClaim claim,
        D045AuthorityCatalogRow expected,
        IReadOnlyList<D045AuthorityCatalogRow> rows)
    {
        if (claim.Ordinal != expected.Ordinal)
            return D045AuthorityCatalogValidationFailure.OrdinalSubstitution;

        if (!IsDefined(claim.Capability))
            return D045AuthorityCatalogValidationFailure.UnknownRow;

        if (claim.Capability != expected.Capability)
        {
            return ContainsCanonicalIdentity(claim, rows)
                ? D045AuthorityCatalogValidationFailure.ReorderedRow
                : D045AuthorityCatalogValidationFailure
                    .CapabilitySubstitution;
        }

        if (!string.Equals(
                claim.StableId,
                expected.StableId,
                StringComparison.Ordinal))
        {
            if (string.Equals(
                    claim.StableId,
                    expected.StableId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return D045AuthorityCatalogValidationFailure.CaseSubstitution;
            }

            return IsKnownStableId(claim.StableId, rows)
                ? D045AuthorityCatalogValidationFailure.ReorderedRow
                : D045AuthorityCatalogValidationFailure.StableIdSubstitution;
        }

        if (!string.Equals(
                claim.DisplayLabel,
                expected.DisplayLabel,
                StringComparison.Ordinal))
        {
            return string.Equals(
                claim.DisplayLabel,
                expected.DisplayLabel,
                StringComparison.OrdinalIgnoreCase)
                ? D045AuthorityCatalogValidationFailure.CaseSubstitution
                : D045AuthorityCatalogValidationFailure
                    .DisplayLabelSubstitution;
        }

        return claim.Disposition == expected.Disposition
            ? D045AuthorityCatalogValidationFailure.None
            : D045AuthorityCatalogValidationFailure
                .DispositionSubstitution;
    }

    private static bool ContainsCanonicalIdentity(
        D045AuthorityCatalogClaim claim,
        IReadOnlyList<D045AuthorityCatalogRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (claim.Capability == rows[index].Capability
                && string.Equals(
                    claim.StableId,
                    rows[index].StableId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownStableId(
        string? stableId,
        IReadOnlyList<D045AuthorityCatalogRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(
                    stableId,
                    rows[index].StableId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static D045AuthorityCatalogRow[] CreateRows()
    {
        var rows = new D045AuthorityCatalogRow[ExactRowCount];
        for (var ordinal = 1; ordinal <= ExactRowCount; ordinal++)
        {
            rows[ordinal - 1] = D045AuthorityCatalogRow.CreateCanonical(
                (D045AuthorityCapability)ordinal);
        }

        return rows;
    }

    private static D045Sha256Fingerprint ComputeFingerprint(
        IReadOnlyList<D045AuthorityCatalogRow> rows)
    {
        var builder = new StringBuilder(
            "D045.AUTHORITY.CATALOG.V1\n",
            2048);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            builder.Append(row.Ordinal.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(
                ((int)row.Capability).ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(row.StableId);
            builder.Append('\t');
            builder.Append(row.DisplayLabel);
            builder.Append('\t');
            builder.Append(
                ((int)row.Disposition).ToString(CultureInfo.InvariantCulture));
            builder.Append('\n');
        }

        return D045Sha256Fingerprint.Create(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
