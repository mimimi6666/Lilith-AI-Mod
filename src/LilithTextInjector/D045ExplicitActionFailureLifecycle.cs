using System;
using System.Collections.Generic;
using System.Linq;
using LilithPermissionControl;

namespace LilithTextInjector;

[Flags]
internal enum D045ExplicitActionGoal
{
    None = 0,
    ReadOrInspect = 1 << 0,
    Open = 1 << 1,
    Create = 1 << 2,
    Delete = 1 << 3,
    Close = 1 << 4,
    Shutdown = 1 << 5,
    Restart = 1 << 6,
    Screenshot = 1 << 7,
    ExternalInput = 1 << 8,
    Unsupported = 1 << 9,
    Ambiguous = 1 << 10,
    PermanentDelete = 1 << 11,
    ForceClose = 1 << 12,
    ReadFile = 1 << 13,
    EnumerateFolder = 1 << 14,
    ClipboardRead = 1 << 15,
    StatusRead = 1 << 16,
    RawShell = 1 << 17,
    Elevation = 1 << 18,
    Workstation = 1 << 19
}

internal enum D045ExplicitOpenTargetKind
{
    Any = 0,
    File = 1,
    Directory = 2,
    Application = 3,
    Ambiguous = 4
}

/// <summary>
/// Provider-neutral terminal reasons for one explicit local-action turn.
/// This vocabulary is independent from provider response formats and from
/// Plugin-private goal classifiers so all action paths can converge on the
/// same truthful failure lifecycle.
/// </summary>
internal enum D045AgentTaskStopReason
{
    None = 0,
    RequiredPermissionDisabled = 1,
    PermissionStateUnavailable = 2,
    ExplicitActionUnsupported = 3,
    ExplicitActionAmbiguous = 4,
    RequiredPermissionRevoked = 5,
    ProviderReturnedWithoutVerifiedReceipt = 6,
    RepeatedNoProgress = 7,
    RoundLimitReached = 8,
    CallLimitReached = 9,
    DeadlineExpired = 10,
    ProviderFaultedBeforeVerification = 11,
    OperationCancelled = 12,
    TargetNotFound = 13,
    TargetAmbiguous = 14,
    GoalIncomplete = 15,
    ToolFailed = 16
}

/// <summary>
/// One required permission clause. Every clause in a requirement must pass,
/// while any enabled capability inside this clause satisfies the clause.
/// </summary>
internal sealed class D045ExplicitActionPermissionClause
{
    private readonly IReadOnlyList<D045PermissionCapability> _anyOf;

    internal string Token { get; }
    internal IReadOnlyList<D045PermissionCapability> AnyOf => _anyOf;

    internal D045ExplicitActionPermissionClause(
        string token,
        params D045PermissionCapability[] anyOf)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A permission-clause token is required.", nameof(token));
        if (anyOf is null || anyOf.Length == 0)
            throw new ArgumentException("A permission clause requires at least one alternative.", nameof(anyOf));

        Token = token.Trim();
        var alternatives = anyOf
            .Distinct()
            .OrderBy(value => (int)value)
            .ToArray();
        foreach (var capability in alternatives)
            D045PermissionControlSnapshot.DemandCapability(capability);
        _anyOf = Array.AsReadOnly(alternatives);
    }

    internal bool IsSatisfiedBy(D045PermissionControlSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));
        return _anyOf.Any(snapshot.IsEnabled);
    }
}

/// <summary>
/// Immutable host-owned permission requirement for a classified explicit
/// action. Clauses are conjunctive; alternatives inside each clause are
/// disjunctive. It deliberately does not depend on Plugin-private enums.
/// </summary>
internal sealed class D045ExplicitActionRequirement
{
    private const D045ExplicitActionGoal KnownGoals =
        D045ExplicitActionGoal.ReadOrInspect
        | D045ExplicitActionGoal.Open
        | D045ExplicitActionGoal.Create
        | D045ExplicitActionGoal.Delete
        | D045ExplicitActionGoal.Close
        | D045ExplicitActionGoal.Shutdown
        | D045ExplicitActionGoal.Restart
        | D045ExplicitActionGoal.Screenshot
        | D045ExplicitActionGoal.ExternalInput
        | D045ExplicitActionGoal.Unsupported
        | D045ExplicitActionGoal.Ambiguous
        | D045ExplicitActionGoal.PermanentDelete
        | D045ExplicitActionGoal.ForceClose
        | D045ExplicitActionGoal.ReadFile
        | D045ExplicitActionGoal.EnumerateFolder
        | D045ExplicitActionGoal.ClipboardRead
        | D045ExplicitActionGoal.StatusRead
        | D045ExplicitActionGoal.RawShell
        | D045ExplicitActionGoal.Elevation
        | D045ExplicitActionGoal.Workstation;

    private readonly IReadOnlyList<D045ExplicitActionPermissionClause> _clauses;

    internal D045ExplicitActionGoal Goals { get; }
    internal D045ExplicitOpenTargetKind OpenTargetKind { get; }
    internal IReadOnlyList<D045ExplicitActionPermissionClause> Clauses
        => _clauses;
    internal bool IsUnsupported
        => (Goals & D045ExplicitActionGoal.Unsupported) != 0
            || (Goals & ~KnownGoals) != 0;
    internal bool IsAmbiguous
        => (Goals & D045ExplicitActionGoal.Ambiguous) != 0
            || (Goals.HasFlag(D045ExplicitActionGoal.Open)
                && OpenTargetKind == D045ExplicitOpenTargetKind.Ambiguous);

    internal D045ExplicitActionRequirement(
        D045ExplicitActionGoal goals,
        D045ExplicitOpenTargetKind openTargetKind =
            D045ExplicitOpenTargetKind.Any)
    {
        if (!Enum.IsDefined(typeof(D045ExplicitOpenTargetKind), openTargetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(openTargetKind));
        }

        Goals = goals;
        OpenTargetKind = openTargetKind;
        _clauses = Array.AsReadOnly(BuildClauses(goals, openTargetKind));
    }

    private static D045ExplicitActionPermissionClause[] BuildClauses(
        D045ExplicitActionGoal goals,
        D045ExplicitOpenTargetKind openTargetKind)
    {
        var clauses = new List<D045ExplicitActionPermissionClause>();
        if (goals.HasFlag(D045ExplicitActionGoal.ReadOrInspect))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "read_or_inspect",
                D045PermissionCapability.SanitizedLilithStatusRead,
                D045PermissionCapability.ClipboardRead,
                D045PermissionCapability.LocalFileRead,
                D045PermissionCapability.LocalFolderEnumerate,
                D045PermissionCapability.ScreenshotCapture));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.ReadFile))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "read_file",
                D045PermissionCapability.LocalFileRead));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.EnumerateFolder))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "enumerate_folder",
                D045PermissionCapability.LocalFolderEnumerate));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.ClipboardRead))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "read_clipboard",
                D045PermissionCapability.ClipboardRead));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.StatusRead))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "read_status",
                D045PermissionCapability.SanitizedLilithStatusRead));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Open))
        {
            clauses.Add(openTargetKind switch
            {
                D045ExplicitOpenTargetKind.File
                    or D045ExplicitOpenTargetKind.Directory =>
                    new D045ExplicitActionPermissionClause(
                        "open",
                        D045PermissionCapability.LocalContentOpen),
                D045ExplicitOpenTargetKind.Application =>
                    new D045ExplicitActionPermissionClause(
                        "open",
                        D045PermissionCapability.ApplicationLaunch),
                _ => new D045ExplicitActionPermissionClause(
                    "open",
                    D045PermissionCapability.ApplicationLaunch,
                    D045PermissionCapability.LocalContentOpen)
            });
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Create))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "create",
                D045PermissionCapability.LocalDataCreate));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Delete))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "recycle_delete",
                D045PermissionCapability.RecycleBinDelete));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.PermanentDelete))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "permanent_delete",
                D045PermissionCapability.PermanentDelete));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Close))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "graceful_close",
                D045PermissionCapability.GracefulProcessClose));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.ForceClose))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "force_close",
                D045PermissionCapability.ForceTerminateProcess));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Shutdown))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "shutdown",
                D045PermissionCapability.Shutdown));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Restart))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "restart",
                D045PermissionCapability.Restart));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Screenshot))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "screenshot",
                D045PermissionCapability.ScreenshotCapture));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.ExternalInput))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "external_input",
                D045PermissionCapability.ArbitraryExternalAppInputKeystrokes));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.RawShell))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "raw_shell",
                D045PermissionCapability.RawShellArbitraryCommand));
        }
        if (goals.HasFlag(D045ExplicitActionGoal.Elevation))
        {
            clauses.Add(new D045ExplicitActionPermissionClause(
                "elevation",
                D045PermissionCapability.GeneralElevationAdminAccess));
        }
        return clauses.ToArray();
    }
}

internal sealed class D045ExplicitActionPermissionPreflightResult
{
    private readonly IReadOnlyList<D045ExplicitActionPermissionClause>
        _missingClauses;

    internal bool Admitted { get; }
    internal D045AgentTaskStopReason StopReason { get; }
    internal long PermissionRevision { get; }
    internal IReadOnlyList<D045ExplicitActionPermissionClause> MissingClauses
        => _missingClauses;

    internal D045ExplicitActionPermissionPreflightResult(
        bool admitted,
        D045AgentTaskStopReason stopReason,
        long permissionRevision,
        IEnumerable<D045ExplicitActionPermissionClause>? missingClauses = null)
    {
        _missingClauses = Array.AsReadOnly((missingClauses
                ?? Array.Empty<D045ExplicitActionPermissionClause>())
            .ToArray());
        if (admitted != (stopReason == D045AgentTaskStopReason.None))
        {
            throw new ArgumentException(
                "Admission and stop reason must describe one consistent outcome.",
                nameof(stopReason));
        }
        if (permissionRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(permissionRevision));
        if (admitted && _missingClauses.Count != 0)
        {
            throw new ArgumentException(
                "An admitted result cannot contain missing clauses.",
                nameof(missingClauses));
        }

        Admitted = admitted;
        StopReason = stopReason;
        PermissionRevision = permissionRevision;
    }
}

internal static class D045ExplicitActionPermissionPreflight
{
    internal static D045ExplicitActionPermissionPreflightResult Evaluate(
        D045ExplicitActionRequirement requirement,
        D045PermissionLoadResult permissionState)
    {
        if (requirement is null)
            throw new ArgumentNullException(nameof(requirement));
        if (permissionState is null)
            throw new ArgumentNullException(nameof(permissionState));

        if (requirement.IsAmbiguous)
        {
            return Stopped(
                D045AgentTaskStopReason.ExplicitActionAmbiguous);
        }
        if (requirement.IsUnsupported)
        {
            return Stopped(
                D045AgentTaskStopReason.ExplicitActionUnsupported);
        }
        if (!IsUsablePermissionState(permissionState))
        {
            return Stopped(
                D045AgentTaskStopReason.PermissionStateUnavailable);
        }

        var snapshot = permissionState.Snapshot;
        var missing = requirement.Clauses
            .Where(clause => !clause.IsSatisfiedBy(snapshot))
            .ToArray();
        return missing.Length == 0
            ? new D045ExplicitActionPermissionPreflightResult(
                admitted: true,
                D045AgentTaskStopReason.None,
                snapshot.Revision)
            : new D045ExplicitActionPermissionPreflightResult(
                admitted: false,
                D045AgentTaskStopReason.RequiredPermissionDisabled,
                snapshot.Revision,
                missing);
    }

    private static D045ExplicitActionPermissionPreflightResult Stopped(
        D045AgentTaskStopReason stopReason)
        => new(
            admitted: false,
            stopReason,
            permissionRevision: 0);

    private static bool IsUsablePermissionState(
        D045PermissionLoadResult permissionState)
    {
        if (!Enum.IsDefined(
                typeof(D045PermissionStoreDisposition),
                permissionState.Disposition)
            || permissionState.Disposition
                == D045PermissionStoreDisposition.InvalidDefaultOff)
        {
            return false;
        }

        var snapshot = permissionState.Snapshot;
        return snapshot.SchemaVersion
                == D045PermissionControlSnapshot.CurrentSchemaVersion
            && snapshot.Revision > 0
            && snapshot.Enabled is { Length: D045PermissionControlSnapshot.ExactCapabilityCount }
            && snapshot.UpdatedUtc.Offset == TimeSpan.Zero;
    }
}

/// <summary>
/// Remembers the exact target identity from a retryable Harness failure and
/// rejects an equivalent retry before any later effect is dispatched. Provider
/// timeouts and presentation-only arguments cannot disguise the same target.
/// </summary>
internal sealed class D045HarnessRetryGate
{
    private readonly object _gate = new();
    private string _pendingCapability = string.Empty;
    private string _pendingTargetFingerprint = string.Empty;

    internal bool HasPendingRetry
    {
        get
        {
            lock (_gate)
                return _pendingCapability.Length > 0;
        }
    }

    internal bool TryAdmit(
        string capability,
        string targetFingerprint)
    {
        DemandIdentity(capability, targetFingerprint);
        lock (_gate)
        {
            return !string.Equals(
                    _pendingCapability,
                    capability,
                    StringComparison.Ordinal)
                || !string.Equals(
                    _pendingTargetFingerprint,
                    targetFingerprint,
                    StringComparison.Ordinal);
        }
    }

    internal void ObserveResult(
        string capability,
        string targetFingerprint,
        bool retryAllowed,
        bool retryRequiresChangedTarget)
    {
        DemandIdentity(capability, targetFingerprint);
        if (retryRequiresChangedTarget && !retryAllowed)
        {
            throw new ArgumentException(
                "A changed-target retry cannot be required when retry is denied.",
                nameof(retryRequiresChangedTarget));
        }
        lock (_gate)
        {
            if (retryAllowed && retryRequiresChangedTarget)
            {
                _pendingCapability = capability;
                _pendingTargetFingerprint = targetFingerprint;
                return;
            }
            if (string.Equals(
                    _pendingCapability,
                    capability,
                    StringComparison.Ordinal))
            {
                _pendingCapability = string.Empty;
                _pendingTargetFingerprint = string.Empty;
            }
        }
    }

    private static void DemandIdentity(
        string capability,
        string targetFingerprint)
    {
        if (string.IsNullOrWhiteSpace(capability))
            throw new ArgumentException("A capability is required.", nameof(capability));
        if (string.IsNullOrWhiteSpace(targetFingerprint))
        {
            throw new ArgumentException(
                "A target fingerprint is required.",
                nameof(targetFingerprint));
        }
    }
}

/// <summary>
/// Converts a completed read-only semantic lookup that cannot yield one exact
/// target into a typed terminal reason. A model must not spend another round
/// repeating an equivalent query that the host already proved missing or
/// ambiguous.
/// </summary>
internal static class D045SemanticObservationFailureLifecycle
{
    internal static bool TryClassifyTerminalFailure(
        bool isSemanticObservation,
        bool succeeded,
        D045HarnessOutcomeReason outcomeReason,
        out D045AgentTaskStopReason stopReason)
    {
        stopReason = outcomeReason switch
        {
            D045HarnessOutcomeReason.TargetNotFound =>
                D045AgentTaskStopReason.TargetNotFound,
            D045HarnessOutcomeReason.TargetAmbiguous =>
                D045AgentTaskStopReason.TargetAmbiguous,
            _ => D045AgentTaskStopReason.None
        };
        return isSemanticObservation
            && !succeeded
            && stopReason != D045AgentTaskStopReason.None;
    }
}
