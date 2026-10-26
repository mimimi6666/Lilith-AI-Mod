using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithTextInjector;

internal enum D029SourceHostStatus
{
    Invalid = 0,
    Off = 1,
    ReadyToArm = 2,
    Armed = 3,
    Running = 4,
    Stopped = 5,
    Completed = 6,
    Fault = 7,
    Capacity = 8
}

internal enum D029SourceArmAdmission
{
    Invalid = 0,
    Armed = 1,
    Disabled = 2,
    AlreadyConsumed = 3,
    RuntimeMismatch = 4,
    ConfigurationMismatch = 5,
    NotYetValid = 6,
    Expired = 7
}

internal enum D029SourceObservationAdmission
{
    Invalid = 0,
    Accepted = 1,
    Completed = 2,
    Stopped = 3,
    Disabled = 4,
    NotArmed = 5,
    Terminal = 6
}

internal sealed class D029SourceHostConfiguration
{
    internal bool Enabled { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ConfigurationFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal string Fingerprint { get; }

    internal D029SourceHostConfiguration(
        bool enabled,
        CharacterRuntimeIdentity runtimeIdentity,
        string configurationFingerprint,
        long configurationGeneration)
    {
        Enabled = enabled;
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ConfigurationFingerprint =
            ProviderRegistryContractGuard.Sha256(
                configurationFingerprint,
                nameof(configurationFingerprint));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/SOURCE-CONFIGURATION/1",
            Enabled ? "1" : "0",
            RuntimeIdentity.RuntimeProfileId,
            RuntimeIdentity.RuntimeInstanceId,
            ProviderRegistryCanonicalHash.Number(
                RuntimeIdentity.Generation),
            RuntimeIdentity.OrchestratorSessionId,
            RuntimeIdentity.OwnerIdentity,
            ConfigurationFingerprint,
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration));
    }
}

internal sealed class D029IntegrationDiagnosticRecord
{
    internal long Sequence { get; }
    internal D029IntegrationStep Step { get; }
    internal D029IntegrationRunState State { get; }
    internal D029IntegrationStopReason StopReason { get; }
    internal int AcceptedSteps { get; }
    internal int ProviderCalls { get; }
    internal string RunFingerprint { get; }
    internal string ObservationFingerprint { get; }
    internal string Fingerprint { get; }

    internal D029IntegrationDiagnosticRecord(
        long sequence,
        D029IntegrationRunIdentity run,
        D029IntegrationStepObservation observation,
        D029IntegrationReplayResult replay)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (run == null)
            throw new ArgumentNullException(nameof(run));
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        if (replay == null)
            throw new ArgumentNullException(nameof(replay));
        Sequence = sequence;
        Step = observation.Step;
        State = replay.State;
        StopReason = replay.StopReason;
        AcceptedSteps = replay.AcceptedSteps;
        ProviderCalls = replay.ProviderCalls;
        RunFingerprint = run.Fingerprint;
        ObservationFingerprint = observation.Fingerprint;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/DIAGNOSTIC-RECORD/1",
            ProviderRegistryCanonicalHash.Number(Sequence),
            ProviderRegistryCanonicalHash.Number((int)Step),
            ProviderRegistryCanonicalHash.Number((int)State),
            ProviderRegistryCanonicalHash.Number((int)StopReason),
            ProviderRegistryCanonicalHash.Number(AcceptedSteps),
            ProviderRegistryCanonicalHash.Number(ProviderCalls),
            RunFingerprint,
            ObservationFingerprint);
    }
}

internal sealed class D029SourceHostSnapshot
{
    internal static D029SourceHostSnapshot Empty { get; } =
        new(
            D029SourceHostStatus.Off,
            processArmConsumed: false,
            run: null,
            Array.Empty<D029IntegrationDiagnosticRecord>());

    internal D029SourceHostStatus Status { get; }
    internal bool ProcessArmConsumed { get; }
    internal D029IntegrationRunIdentity? Run { get; }
    internal IReadOnlyList<D029IntegrationDiagnosticRecord> Records {
        get;
    }
    internal int RecordCount => Records.Count;
    internal D029IntegrationDiagnosticRecord? Latest =>
        Records.Count == 0 ? null : Records[^1];
    internal D029IntegrationAuthorityCounters AuthorityOutputs =>
        D029IntegrationAuthorityCounters.Zero;

    internal D029SourceHostSnapshot(
        D029SourceHostStatus status,
        bool processArmConsumed,
        D029IntegrationRunIdentity? run,
        IReadOnlyList<D029IntegrationDiagnosticRecord> records)
    {
        if (!Enum.IsDefined(typeof(D029SourceHostStatus), status)
            || status == D029SourceHostStatus.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        Status = status;
        ProcessArmConsumed = processArmConsumed;
        Run = run;
        Records = new ReadOnlyCollection<D029IntegrationDiagnosticRecord>(
            records == null
                ? throw new ArgumentNullException(nameof(records))
                : records.ToArray());
    }
}

internal sealed class D029AggressiveIntegrationSourceHost
{
    private readonly object _gate = new();
    private readonly List<D029IntegrationStepObservation> _observations =
        new();
    private readonly Queue<D029IntegrationDiagnosticRecord> _records =
        new();
    private D029SourceHostConfiguration? _configuration;
    private D029IntegrationRunIdentity? _run;
    private D029SourceHostStatus _status = D029SourceHostStatus.Off;
    private bool _processArmConsumed;
    private long _diagnosticSequence;

    internal void Reconcile(D029SourceHostConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            if (!configuration.Enabled)
            {
                _configuration = configuration;
                ClearRun();
                _status = D029SourceHostStatus.Off;
                return;
            }
            if (_configuration != null
                && !string.Equals(
                    _configuration.Fingerprint,
                    configuration.Fingerprint,
                    StringComparison.Ordinal)
                && _run != null)
            {
                ClearRun();
                _configuration = configuration;
                _status = D029SourceHostStatus.Stopped;
                return;
            }
            _configuration = configuration;
            if (_run == null)
            {
                _status = _processArmConsumed
                    ? D029SourceHostStatus.Capacity
                    : D029SourceHostStatus.ReadyToArm;
            }
        }
    }

    internal D029SourceArmAdmission Arm(
        D029IntegrationRunIdentity run,
        DateTimeOffset now)
    {
        if (run == null)
            throw new ArgumentNullException(nameof(run));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (_configuration == null || !_configuration.Enabled)
                return D029SourceArmAdmission.Disabled;
            if (_processArmConsumed)
                return D029SourceArmAdmission.AlreadyConsumed;
            if (now < run.StartedAt)
                return D029SourceArmAdmission.NotYetValid;
            if (now >= run.ExpiresAt)
                return D029SourceArmAdmission.Expired;
            if (!_configuration.RuntimeIdentity.Matches(
                    run.RuntimeIdentity))
            {
                return D029SourceArmAdmission.RuntimeMismatch;
            }
            if (_configuration.ConfigurationGeneration
                    != run.ConfigurationGeneration
                || !string.Equals(
                    _configuration.ConfigurationFingerprint,
                    run.ConfigurationFingerprint,
                    StringComparison.Ordinal))
            {
                return D029SourceArmAdmission.ConfigurationMismatch;
            }
            _processArmConsumed = true;
            _run = run;
            _observations.Clear();
            _records.Clear();
            _diagnosticSequence = 0;
            _status = D029SourceHostStatus.Armed;
            return D029SourceArmAdmission.Armed;
        }
    }

    internal D029SourceObservationAdmission Observe(
        D029IntegrationStepObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        lock (_gate)
        {
            if (_configuration == null || !_configuration.Enabled)
                return D029SourceObservationAdmission.Disabled;
            if (_run == null
                || (_status != D029SourceHostStatus.Armed
                    && _status != D029SourceHostStatus.Running))
            {
                return IsTerminal(_status)
                    ? D029SourceObservationAdmission.Terminal
                    : D029SourceObservationAdmission.NotArmed;
            }

            _observations.Add(observation);
            var replay = D029AggressiveIntegrationReplay.Run(
                D029IntegrationPlan.Accepted,
                _run,
                _observations);
            AppendDiagnostic(
                new D029IntegrationDiagnosticRecord(
                    ++_diagnosticSequence,
                    _run,
                    observation,
                    replay));
            _status = Map(replay.State);
            return replay.State switch
            {
                D029IntegrationRunState.Completed =>
                    D029SourceObservationAdmission.Completed,
                D029IntegrationRunState.Stopped
                    or D029IntegrationRunState.Fault
                    or D029IntegrationRunState.Capacity =>
                    D029SourceObservationAdmission.Stopped,
                _ => D029SourceObservationAdmission.Accepted
            };
        }
    }

    internal D029SourceHostSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new D029SourceHostSnapshot(
                _status,
                _processArmConsumed,
                _run,
                _records.ToArray());
        }
    }

    private void AppendDiagnostic(
        D029IntegrationDiagnosticRecord record)
    {
        while (_records.Count
            >= D029IntegrationCeilings.MaximumDiagnosticRecords)
        {
            _records.Dequeue();
        }
        _records.Enqueue(record);
    }

    private void ClearRun()
    {
        _run = null;
        _observations.Clear();
        _records.Clear();
        _diagnosticSequence = 0;
    }

    private static bool IsTerminal(D029SourceHostStatus status)
        => status is D029SourceHostStatus.Stopped
            or D029SourceHostStatus.Completed
            or D029SourceHostStatus.Fault
            or D029SourceHostStatus.Capacity;

    private static D029SourceHostStatus Map(
        D029IntegrationRunState state)
        => state switch
        {
            D029IntegrationRunState.Running =>
                D029SourceHostStatus.Running,
            D029IntegrationRunState.Completed =>
                D029SourceHostStatus.Completed,
            D029IntegrationRunState.Stopped =>
                D029SourceHostStatus.Stopped,
            D029IntegrationRunState.Fault =>
                D029SourceHostStatus.Fault,
            D029IntegrationRunState.Capacity =>
                D029SourceHostStatus.Capacity,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
}
