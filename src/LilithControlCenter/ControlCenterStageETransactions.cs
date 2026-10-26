using System;
using System.Collections.Generic;

namespace LilithControlCenter;

internal enum PresentationDensity
{
    Invalid = 0,
    Comfortable = 1,
    Compact = 2
}

internal enum PresentationTransactionAdmission
{
    Invalid = 0,
    Applied = 1,
    AlreadyEffective = 2,
    SettingNotAllowed = 3,
    DesiredRevisionMismatch = 4,
    ConfigurationGenerationMismatch = 5,
    RuntimeIdentityMismatch = 6,
    RequestNotYetValid = 7,
    RequestExpired = 8,
    DuplicateRequest = 9,
    CapacityReached = 10
}

internal enum PresentationRollbackAdmission
{
    Invalid = 0,
    RolledBack = 1,
    UnknownRollbackToken = 2,
    RollbackAlreadyConsumed = 3,
    RuntimeIdentityMismatch = 4,
    StateAdvanced = 5
}

internal sealed class PresentationSettingsSnapshot
{
    internal const string DensitySettingId = "presentation.density";

    internal string RuntimeIdentity { get; }
    internal long RuntimeGeneration { get; }
    internal long DesiredRevision { get; }
    internal long ConfigurationGeneration { get; }
    internal PresentationDensity EffectiveDensity { get; }

    internal PresentationSettingsSnapshot(
        string runtimeIdentity,
        long runtimeGeneration,
        long desiredRevision,
        long configurationGeneration,
        PresentationDensity effectiveDensity)
    {
        RuntimeIdentity = Required(runtimeIdentity, nameof(runtimeIdentity));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        if (desiredRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(desiredRevision));
        if (configurationGeneration < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        if (effectiveDensity is not (
                PresentationDensity.Comfortable
                or PresentationDensity.Compact))
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveDensity));
        }
        RuntimeGeneration = runtimeGeneration;
        DesiredRevision = desiredRevision;
        ConfigurationGeneration = configurationGeneration;
        EffectiveDensity = effectiveDensity;
    }

    internal static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("A bounded identity is required.", parameterName);
        return value;
    }
}

internal sealed class PresentationSettingsTransaction
{
    internal string RequestId { get; }
    internal string SettingId { get; }
    internal PresentationDensity DesiredDensity { get; }
    internal long ExpectedDesiredRevision { get; }
    internal long ExpectedConfigurationGeneration { get; }
    internal string ExpectedRuntimeIdentity { get; }
    internal long ExpectedRuntimeGeneration { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal PresentationSettingsTransaction(
        string requestId,
        string settingId,
        PresentationDensity desiredDensity,
        long expectedDesiredRevision,
        long expectedConfigurationGeneration,
        string expectedRuntimeIdentity,
        long expectedRuntimeGeneration,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        RequestId = PresentationSettingsSnapshot.Required(
            requestId,
            nameof(requestId));
        SettingId = PresentationSettingsSnapshot.Required(
            settingId,
            nameof(settingId));
        DesiredDensity = desiredDensity;
        ExpectedDesiredRevision = expectedDesiredRevision;
        ExpectedConfigurationGeneration = expectedConfigurationGeneration;
        ExpectedRuntimeIdentity = PresentationSettingsSnapshot.Required(
            expectedRuntimeIdentity,
            nameof(expectedRuntimeIdentity));
        ExpectedRuntimeGeneration = expectedRuntimeGeneration;
        RequestedAt = requestedAt;
        ExpiresAt = expiresAt;
    }
}

internal sealed class PresentationSettingsReceipt
{
    internal PresentationTransactionAdmission Admission { get; }
    internal string RequestId { get; }
    internal PresentationDensity DesiredDensity { get; }
    internal PresentationDensity EffectiveDensity { get; }
    internal long DesiredRevision { get; }
    internal long ConfigurationGeneration { get; }
    internal string RollbackToken { get; }

    internal PresentationSettingsReceipt(
        PresentationTransactionAdmission admission,
        string requestId,
        PresentationDensity desiredDensity,
        PresentationDensity effectiveDensity,
        long desiredRevision,
        long configurationGeneration,
        string rollbackToken)
    {
        Admission = admission;
        RequestId = requestId;
        DesiredDensity = desiredDensity;
        EffectiveDensity = effectiveDensity;
        DesiredRevision = desiredRevision;
        ConfigurationGeneration = configurationGeneration;
        RollbackToken = rollbackToken;
    }
}

internal sealed class PresentationRollbackReceipt
{
    internal PresentationRollbackAdmission Admission { get; }
    internal PresentationDensity EffectiveDensity { get; }
    internal long DesiredRevision { get; }
    internal long ConfigurationGeneration { get; }

    internal PresentationRollbackReceipt(
        PresentationRollbackAdmission admission,
        PresentationDensity effectiveDensity,
        long desiredRevision,
        long configurationGeneration)
    {
        Admission = admission;
        EffectiveDensity = effectiveDensity;
        DesiredRevision = desiredRevision;
        ConfigurationGeneration = configurationGeneration;
    }
}

internal sealed class ControlCenterPresentationRuntime
{
    internal const int MaximumRecordedRequests = 64;
    internal static readonly TimeSpan MaximumRequestLifetime =
        TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly string _runtimeIdentity;
    private readonly long _runtimeGeneration;
    private readonly HashSet<string> _requestIds =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RollbackRecord> _rollbacks =
        new(StringComparer.Ordinal);
    private long _desiredRevision;
    private long _configurationGeneration;
    private PresentationDensity _density;

    internal ControlCenterPresentationRuntime(
        string runtimeIdentity,
        long runtimeGeneration)
    {
        _runtimeIdentity = PresentationSettingsSnapshot.Required(
            runtimeIdentity,
            nameof(runtimeIdentity));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        _runtimeGeneration = runtimeGeneration;
        _density = PresentationDensity.Comfortable;
    }

    internal PresentationSettingsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return SnapshotLocked();
        }
    }

    internal PresentationSettingsReceipt Apply(
        PresentationSettingsTransaction? transaction,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            if (transaction == null)
                return Receipt(PresentationTransactionAdmission.Invalid, null);
            if (!string.Equals(
                    transaction.SettingId,
                    PresentationSettingsSnapshot.DensitySettingId,
                    StringComparison.Ordinal)
                || transaction.DesiredDensity is not (
                    PresentationDensity.Comfortable
                    or PresentationDensity.Compact))
            {
                return Receipt(
                    PresentationTransactionAdmission.SettingNotAllowed,
                    transaction);
            }
            if (!string.Equals(
                    transaction.ExpectedRuntimeIdentity,
                    _runtimeIdentity,
                    StringComparison.Ordinal)
                || transaction.ExpectedRuntimeGeneration != _runtimeGeneration)
            {
                return Receipt(
                    PresentationTransactionAdmission.RuntimeIdentityMismatch,
                    transaction);
            }
            if (transaction.ExpiresAt <= transaction.RequestedAt
                || transaction.ExpiresAt - transaction.RequestedAt
                    > MaximumRequestLifetime)
            {
                return Receipt(
                    PresentationTransactionAdmission.Invalid,
                    transaction);
            }
            if (now < transaction.RequestedAt)
            {
                return Receipt(
                    PresentationTransactionAdmission.RequestNotYetValid,
                    transaction);
            }
            if (now >= transaction.ExpiresAt)
            {
                return Receipt(
                    PresentationTransactionAdmission.RequestExpired,
                    transaction);
            }
            if (_requestIds.Contains(transaction.RequestId))
            {
                return Receipt(
                    PresentationTransactionAdmission.DuplicateRequest,
                    transaction);
            }
            if (_requestIds.Count >= MaximumRecordedRequests)
            {
                return Receipt(
                    PresentationTransactionAdmission.CapacityReached,
                    transaction);
            }
            if (transaction.ExpectedDesiredRevision != _desiredRevision)
            {
                return Receipt(
                    PresentationTransactionAdmission.DesiredRevisionMismatch,
                    transaction);
            }
            if (transaction.ExpectedConfigurationGeneration
                != _configurationGeneration)
            {
                return Receipt(
                    PresentationTransactionAdmission
                        .ConfigurationGenerationMismatch,
                    transaction);
            }

            _requestIds.Add(transaction.RequestId);
            if (transaction.DesiredDensity == _density)
            {
                return Receipt(
                    PresentationTransactionAdmission.AlreadyEffective,
                    transaction);
            }

            var previous = _density;
            _density = transaction.DesiredDensity;
            _desiredRevision++;
            _configurationGeneration++;
            var token = "rollback-" + Guid.NewGuid().ToString("N");
            _rollbacks.Add(
                token,
                new RollbackRecord(
                    previous,
                    _desiredRevision,
                    _configurationGeneration));
            return new PresentationSettingsReceipt(
                PresentationTransactionAdmission.Applied,
                transaction.RequestId,
                transaction.DesiredDensity,
                _density,
                _desiredRevision,
                _configurationGeneration,
                token);
        }
    }

    internal PresentationRollbackReceipt Rollback(
        string? rollbackToken,
        string? expectedRuntimeIdentity,
        long expectedRuntimeGeneration)
    {
        lock (_gate)
        {
            if (!string.Equals(
                    expectedRuntimeIdentity,
                    _runtimeIdentity,
                    StringComparison.Ordinal)
                || expectedRuntimeGeneration != _runtimeGeneration)
            {
                return RollbackReceipt(
                    PresentationRollbackAdmission.RuntimeIdentityMismatch);
            }
            if (string.IsNullOrWhiteSpace(rollbackToken)
                || !_rollbacks.TryGetValue(rollbackToken, out var record))
            {
                return RollbackReceipt(
                    PresentationRollbackAdmission.UnknownRollbackToken);
            }
            if (record.Consumed)
            {
                return RollbackReceipt(
                    PresentationRollbackAdmission.RollbackAlreadyConsumed);
            }
            if (_desiredRevision != record.AppliedDesiredRevision
                || _configurationGeneration
                    != record.AppliedConfigurationGeneration)
            {
                return RollbackReceipt(
                    PresentationRollbackAdmission.StateAdvanced);
            }
            record.Consumed = true;
            _density = record.PreviousDensity;
            _desiredRevision++;
            _configurationGeneration++;
            return RollbackReceipt(
                PresentationRollbackAdmission.RolledBack);
        }
    }

    private PresentationSettingsReceipt Receipt(
        PresentationTransactionAdmission admission,
        PresentationSettingsTransaction? transaction)
        => new(
            admission,
            transaction?.RequestId ?? string.Empty,
            transaction?.DesiredDensity ?? PresentationDensity.Invalid,
            _density,
            _desiredRevision,
            _configurationGeneration,
            string.Empty);

    private PresentationRollbackReceipt RollbackReceipt(
        PresentationRollbackAdmission admission)
        => new(
            admission,
            _density,
            _desiredRevision,
            _configurationGeneration);

    private PresentationSettingsSnapshot SnapshotLocked()
        => new(
            _runtimeIdentity,
            _runtimeGeneration,
            _desiredRevision,
            _configurationGeneration,
            _density);

    private sealed class RollbackRecord
    {
        internal PresentationDensity PreviousDensity { get; }
        internal long AppliedDesiredRevision { get; }
        internal long AppliedConfigurationGeneration { get; }
        internal bool Consumed { get; set; }

        internal RollbackRecord(
            PresentationDensity previousDensity,
            long appliedDesiredRevision,
            long appliedConfigurationGeneration)
        {
            PreviousDensity = previousDensity;
            AppliedDesiredRevision = appliedDesiredRevision;
            AppliedConfigurationGeneration = appliedConfigurationGeneration;
        }
    }
}

internal readonly struct ControlCenterStageEAuthorityCounters
{
    internal int GameConfigurationWrites { get; }
    internal int CredentialReads { get; }
    internal int ProviderCalls { get; }
    internal int PromptWrites { get; }
    internal int MemoryWrites { get; }
    internal int DecisionOutputs { get; }
    internal int QueueWrites { get; }
    internal int MotionOutputs { get; }
    internal int ExecutionOutputs { get; }
    internal int PlayerDataWrites { get; }
    internal int DesktopOutputs { get; }

    internal static ControlCenterStageEAuthorityCounters Zero => default;
}
