using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

public sealed class LilithRuntimeService :
    IAsyncDisposable,
    ILilithRuntimeServeRuntime
{
    private readonly LilithRuntimeOptions _options;
    private readonly ILilithHermesProfileEnvironment _profileEnvironment;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly LilithRuntimeVoiceCaptureArbiter _voiceCaptureArbiter = new();
    private readonly LilithRuntimeModelAuthority _modelAuthority = new();

    private WindowsJobProcess? _jobProcess;
    private Task? _stdoutDrainTask;
    private Task? _stderrDrainTask;
    private WindowsJobProcess? _messagingJobProcess;
    private Task? _messagingStdoutDrainTask;
    private Task? _messagingStderrDrainTask;
    private HermesGatewayClient? _gateway;
    private string? _readyFilePath;
    private Guid _generationId;
    private int _port;
    private int _stdoutLineCount;
    private int _stderrLineCount;
    private string _providerInstanceId = string.Empty;
    private long _providerConfigurationGeneration;
    private LilithProfileProvisioningReceipt? _profile;
    private bool _disposed;

    public LilithRuntimeState State { get; private set; } =
        LilithRuntimeState.Stopped;

    public event Action<HermesGatewayEvent>? GatewayEvent;

    internal LilithDailyNoteApprovalBroker DailyNoteApprovals { get; }
    internal LilithLettersCoordinator? Letters { get; set; }
    private LilithDailyNoteDiagnostic? _dailyNoteDiagnostic;
    internal LilithDailyNoteDiagnostic? DailyNoteDiagnostic
    {
        get
        {
            var value = Volatile.Read(ref _dailyNoteDiagnostic);
            return value?.GenerationId == _generationId ? value : null;
        }
    }
    internal void PublishDailyNoteDiagnostic(LilithDailyNoteDiagnostic value)
    {
        if (value.GenerationId == _generationId)
            Volatile.Write(ref _dailyNoteDiagnostic, value);
    }

    public LilithRuntimeService(LilithRuntimeOptions? options = null)
        : this(options, null)
    {
    }

    internal LilithRuntimeService(
        LilithRuntimeOptions? options,
        ILilithHermesProfileEnvironment? profileEnvironment)
    {
        _options = options ?? LilithRuntimeOptions.CreateDefault();
        _options.Validate();
        _profileEnvironment = profileEnvironment
            ?? new LilithCurrentUserHermesProfileEnvironment(
                _options.HermesHomePath);
        DailyNoteApprovals = new(() => _generationId, () => State == LilithRuntimeState.Ready);
    }

    public string EffectiveProviderId
    {
        get => _modelAuthority.Snapshot().ProviderId;
    }

    public string EffectiveModelId
    {
        get => _modelAuthority.Snapshot().ModelId;
    }

    internal (string ProviderId, string ModelId) EffectiveModelIdentity()
    {
        var snapshot = _modelAuthority.Snapshot();
        return (snapshot.ProviderId, snapshot.ModelId);
    }

    internal LilithRuntimeApprovedModelCatalog ApprovedModelCatalog
    {
        get => _modelAuthority.Snapshot().Catalog
            ?? throw new LilithRuntimeException(
                "Lilith Runtime has no approved provider catalog.");
    }

    internal LilithRuntimeModelAuthority ModelAuthority => _modelAuthority;

    internal LilithRuntimeVoiceCaptureArbiter VoiceCaptureArbiter =>
        _voiceCaptureArbiter;

    public async Task<LilithRuntimeSnapshot> StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Refuse before any verification or plugin provisioning can write.
            LilithPreviousConfigRecovery.ThrowIfPending(_options.HermesHomePath);
            LilithIdentityTransitionGuard.ThrowIfPending(_options.HermesHomePath);
            if (State == LilithRuntimeState.Ready)
            {
                return Snapshot();
            }

            if (State is LilithRuntimeState.Starting or LilithRuntimeState.Stopping)
            {
                throw new LilithRuntimeException(
                    "Lilith Runtime is already changing lifecycle state.");
            }

            State = LilithRuntimeState.Starting;
            _voiceCaptureArbiter.Reset();
            _generationId = Guid.NewGuid();
            _stdoutLineCount = 0;
            _stderrLineCount = 0;
            _modelAuthority.BeginGeneration(_generationId);
            _providerInstanceId = string.Empty;
            _providerConfigurationGeneration = 0;
            var startupFailureKind = LilithRuntimeFailureKind.HermesComponentLayoutMismatch;
            try
            {
                await HermesPinnedInstallation.VerifyAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);
                startupFailureKind = LilithRuntimeFailureKind.CuaComponentVerificationFailed;
                await PinnedCuaDriverInstallation.VerifyAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);
                startupFailureKind = LilithRuntimeFailureKind.VoiceComponentVerificationFailed;
                await PinnedHermesVoiceInstallation.VerifyAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);
                startupFailureKind = LilithRuntimeFailureKind.ProfileVerificationFailed;
                await LilithNotesPluginProvisioner.ProvisionAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);
                await LilithLeaveNotePluginProvisioner.ProvisionAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);
                _profile = await LilithProfileProvisioner.ProvisionAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);
                await LilithRelationshipMemoryProvisioner.ProvisionAsync(
                    _options,
                    cancellationToken).ConfigureAwait(false);

                LilithRuntimeWorkspacePathGuard.EnsureSafe(
                    Path.Combine(_options.HermesHomePath, "workspace"));

                var runtimeDirectory = Path.Combine(
                    _options.HermesHomePath,
                    "runtime");
                Directory.CreateDirectory(runtimeDirectory);
                _readyFilePath = Path.Combine(
                    runtimeDirectory,
                    $"serve-ready-{_generationId:N}.json");
                DeleteExactFile(_readyFilePath);

                // Runtime startup binds only the dedicated Hermes profile.
                // Provider credentials, provider/model selection and the model
                // catalog are native Hermes authority. This deliberately keeps
                // the management pipe available on a clean installation with
                // no legacy D033 store, route or API key.
                using var profileLease = _profileEnvironment.Open();

                var sessionToken = CreateSessionToken();
                var runtimeTrustToken = CreateSessionToken();
                startupFailureKind = LilithRuntimeFailureKind.GatewayStartupFailed;
                _jobProcess = StartHermesProcess(
                    sessionToken,
                    runtimeTrustToken,
                    _readyFilePath,
                    profileLease);

                _port = await WaitForReadyPortAsync(
                    _jobProcess.Process,
                    _readyFilePath,
                    _options.StartupTimeout,
                    cancellationToken).ConfigureAwait(false);

                var gateway = new HermesGatewayClient();
                gateway.EventReceived += ForwardGatewayEvent;
                var uri = new Uri(
                    $"ws://127.0.0.1:{_port}/api/ws?token="
                    + Uri.EscapeDataString(sessionToken));
                await gateway.ConnectAsync(
                    uri,
                    runtimeTrustToken,
                    _options.StartupTimeout,
                    cancellationToken).ConfigureAwait(false);
                _gateway = gateway;

                startupFailureKind = LilithRuntimeFailureKind.NativeSurfaceVerificationFailed;
                var modelOptions = await gateway.RequestAsync(
                    "model.options",
                    new
                    {
                        explicit_only = false,
                        include_unconfigured = true,
                        refresh = false
                    },
                    _options.GatewayRequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                ApplyNativeModelCatalog(
                    HermesNativeStartupAttestation.InspectModelCatalog(
                        modelOptions).ApprovedModelCatalog);

                try
                {
                    var telegramStatus = await gateway.RequestAsync(
                        "messaging.telegram.status",
                        new { },
                        _options.GatewayRequestTimeout,
                        cancellationToken).ConfigureAwait(false);
                    if (ShouldStartTelegramGateway(telegramStatus))
                    {
                        TryStartTelegramGateway();
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Telegram is an optional native Hermes channel. A missing,
                    // old or temporarily unavailable messaging surface must not
                    // take the local workbench, F6/F7, or F10 offline with it.
                }

                State = LilithRuntimeState.Ready;
                return Snapshot();
            }
            catch (Exception exception)
            {
                State = LilithRuntimeState.Faulted;
                await StopCoreAsync(
                    keepFaultedState: true,
                    terminateImmediately: false,
                    CancellationToken.None).ConfigureAwait(false);
                if (exception is OperationCanceledException
                    && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw LilithRuntimeStartupFailure.Classify(exception, startupFailureKind);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var gateway = RequireReadyGateway();
        return await gateway.RequestAsync(
            method,
            parameters,
            _options.GetGatewayRequestTimeout(method),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<HermesNativeSurfaceProbeReceipt> ProbeNativeSurfaceAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var modelAuthority = await _modelAuthority.AcquireAsync(
                _generationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (modelAuthority.Snapshot.GenerationId != _generationId
            || modelAuthority.Snapshot.State
                != LilithRuntimeModelAuthorityState.Ready)
        {
            throw new LilithRuntimeException(
                "Lilith Runtime has no usable model authority for this generation.");
        }
        var gateway = RequireReadyGateway();

        var credentialStatus = await gateway.RequestAsync(
            "model.credential_status",
            new { },
            _options.GatewayRequestTimeout,
            cancellationToken).ConfigureAwait(false);
        var commands = await gateway.RequestAsync(
            "commands.catalog",
            new { },
            _options.GatewayRequestTimeout,
            cancellationToken).ConfigureAwait(false);
        var models = await gateway.RequestAsync(
            "model.options",
            new
            {
                explicit_only = false,
                include_unconfigured = true,
                refresh = false
            },
            _options.GatewayRequestTimeout,
            cancellationToken).ConfigureAwait(false);
        var nativeToolsPayload = await gateway.RequestAsync(
            "tools.catalog",
            new { },
            _options.GatewayRequestTimeout,
            cancellationToken).ConfigureAwait(false);
        var attestation = HermesNativeStartupAttestation.Inspect(
            credentialStatus,
            commands,
            models,
            nativeToolsPayload);
        ApplyNativeModelCatalog(
            attestation.ApprovedModelCatalog,
            modelAuthority);

        // Startup attests only provider-independent Hermes catalogs. Creating
        // an agent here would make the management pipe depend on whichever
        // model credential happens to be selected, preventing the user from
        // repairing credentials through Control Center. Every real chat
        // session is still independently rejected until its session.info
        // event proves the bounded native tool surface and fixed workspace.
        return new HermesNativeSurfaceProbeReceipt(
            _generationId,
            SessionId: string.Empty,
            attestation.CommandCount,
            attestation.ModelProviderCount,
            SessionStatusReceived: false,
            SessionNativeSurfaceValid: false,
            NativeToolCount: 0,
            NativeToolsetCount: 0,
            McpServerCount: 0,
            NativeCatalogToolsetCount: attestation.NativeTools.Toolsets.Count,
            NativeApprovalMode: HermesNativeToolsContract.ApprovalModeToken(
                attestation.NativeTools.ApprovalMode),
            NoPromptSubmitted: true,
            CredentialStatusReceived: true,
            attestation.ApiKeyProviderConfigured,
            CatalogSurfaceValid: true);
    }

    private void ApplyNativeModelCatalog(
        LilithRuntimeApprovedModelCatalog catalog,
        LilithRuntimeModelAuthorityLease? lease = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var published = lease is null
            ? _modelAuthority.PublishCatalog(_generationId, catalog)
            : _modelAuthority.PublishCatalog(lease, _generationId, catalog);
        if (!published)
        {
            throw new LilithRuntimeException(
                "Lilith Runtime could not publish the attested model catalog.");
        }
        _providerInstanceId = string.Empty;
        _providerConfigurationGeneration = 0;
    }

    public async Task<HermesStopReceipt> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StopCoreAsync(
                keepFaultedState: false,
                terminateImmediately: false,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<HermesStopReceipt> StopCredentialMutationGenerationAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StopCoreAsync(
                keepFaultedState: false,
                terminateImmediately: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<HermesStopReceipt> StopVoiceRecoveryGenerationAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StopCoreAsync(
                keepFaultedState: false,
                terminateImmediately: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            _lifecycle.Dispose();
        }
    }

    private WindowsJobProcess StartHermesProcess(
        string sessionToken,
        string runtimeTrustToken,
        string readyFilePath,
        ILilithHermesProfileEnvironmentLease profileLease)
    {
        var workspacePath = Path.Combine(_options.HermesHomePath, "workspace");
        LilithRuntimeWorkspacePathGuard.EnsureSafe(workspacePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.HermesExecutablePath,
            WorkingDirectory = workspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ConfigurePortablePythonEntrypoint(startInfo);
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--isolated");
        startInfo.ArgumentList.Add("--skip-build");

        ConfigureNativeToolEnvironment(startInfo);
        startInfo.Environment["HERMES_DASHBOARD_SESSION_TOKEN"] = sessionToken;
        startInfo.Environment["HERMES_LILITH_RUNTIME_TRUST_TOKEN"] =
            runtimeTrustToken;
        startInfo.Environment["HERMES_DESKTOP_READY_FILE"] = readyFilePath;
        startInfo.Environment["HERMES_PARENT_PID"] =
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["HERMES_SERVE_WATCHDOG_POLL_S"] = "0.5";
        ScrubInheritedIdentityEnvironment(startInfo, _options);
        ConfigurePinnedCheckpointEnvironment(startInfo);
        ConfigurePinnedVoiceEnvironment(startInfo, _options);
        ConfigurePortablePythonEnvironment(startInfo, _options);

        profileLease.ApplyTo(startInfo);
        ConfigurePinnedIdentityEnvironment(
            startInfo,
            _options,
            (_profile ?? throw new InvalidOperationException(
                "The strict identity profile receipt is unavailable."))
            .LearningConfigSha256);
        startInfo.Environment["HERMES_CUA_DRIVER_CMD"] =
            _options.CuaDriverExecutablePath;
        LilithTelegramVoiceProfileProjection.ApplyToChildEnvironment(startInfo);
        WindowsJobProcess? process = null;
        try
        {
            process = WindowsJobObject.StartProcess(startInfo);
        }
        finally
        {
            try
            {
                profileLease.ScrubFrom(startInfo);
            }
            finally
            {
                startInfo.Environment.Remove(
                    "HERMES_DASHBOARD_SESSION_TOKEN");
                startInfo.Environment.Remove(
                    "HERMES_LILITH_RUNTIME_TRUST_TOKEN");
            }
        }

        _stdoutDrainTask = DrainOutputAsync(
            process.StandardOutput,
            isStandardOutput: true);
        _stderrDrainTask = DrainOutputAsync(
            process.StandardError,
            isStandardOutput: false);
        return process;
    }

    private void TryStartTelegramGateway()
    {
        try
        {
            // A native profile binding is intentionally single-use. The TUI
            // process consumed the startup binding already, so the messaging
            // process must obtain its own binding instead of attempting to
            // reuse the retired lease.
            _messagingJobProcess = RunWithDedicatedProfileLease(
                _profileEnvironment,
                StartTelegramGatewayProcess);
        }
        catch
        {
            // Telegram is an optional native Hermes channel. A launch fault is
            // reflected by the redacted messaging status surface and must not
            // take the local workbench, F6/F7, or F10 offline with it.
            _messagingJobProcess = null;
            _messagingStdoutDrainTask = null;
            _messagingStderrDrainTask = null;
        }
    }

    internal static TResult RunWithDedicatedProfileLease<TResult>(
        ILilithHermesProfileEnvironment profileEnvironment,
        Func<ILilithHermesProfileEnvironmentLease, TResult> action)
    {
        ArgumentNullException.ThrowIfNull(profileEnvironment);
        ArgumentNullException.ThrowIfNull(action);
        using var profileLease = profileEnvironment.Open();
        return action(profileLease);
    }

    private WindowsJobProcess StartTelegramGatewayProcess(
        ILilithHermesProfileEnvironmentLease profileLease)
    {
        var workspacePath = Path.Combine(_options.HermesHomePath, "workspace");
        LilithRuntimeWorkspacePathGuard.EnsureSafe(workspacePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.HermesExecutablePath,
            WorkingDirectory = workspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ConfigurePortablePythonEntrypoint(startInfo);
        startInfo.ArgumentList.Add("gateway");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--external-supervisor");

        ConfigureNativeToolEnvironment(startInfo);
        // The TUI process remains the one local cron ticker. The messaging
        // process only transports native channel events into the same durable
        // session, preventing duplicate scheduled-task delivery.
        startInfo.Environment["HERMES_CRON_TICKER"] = "0";
        startInfo.Environment.Remove("HERMES_DASHBOARD_SESSION_TOKEN");
        startInfo.Environment.Remove("HERMES_DESKTOP_READY_FILE");
        startInfo.Environment["HERMES_PARENT_PID"] =
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ScrubInheritedIdentityEnvironment(startInfo, _options);
        ConfigurePinnedCheckpointEnvironment(startInfo);
        ConfigurePinnedVoiceEnvironment(startInfo, _options);
        ConfigurePortablePythonEnvironment(startInfo, _options);

        profileLease.ApplyTo(startInfo);
        ConfigurePinnedIdentityEnvironment(
            startInfo,
            _options,
            (_profile ?? throw new InvalidOperationException(
                "The strict identity profile receipt is unavailable."))
            .LearningConfigSha256);
        startInfo.Environment["HERMES_CUA_DRIVER_CMD"] =
            _options.CuaDriverExecutablePath;

        LilithTelegramVoiceProfileProjection.ApplyToChildEnvironment(startInfo);
        WindowsJobProcess? process = null;
        try
        {
            process = WindowsJobObject.StartProcess(startInfo);
        }
        finally
        {
            try
            {
                profileLease.ScrubFrom(startInfo);
            }
            catch
            {
                // Closing a kill-on-close Job is the transaction rollback for
                // a child that started before credential scrubbing failed.
                process?.Dispose();
                process = null;
                throw;
            }
        }

        var boundProcess = process
            ?? throw new InvalidOperationException(
                "Hermes Telegram gateway did not return a process handle.");
        _messagingStdoutDrainTask = DrainOutputAsync(
            boundProcess.StandardOutput,
            isStandardOutput: true);
        _messagingStderrDrainTask = DrainOutputAsync(
            boundProcess.StandardError,
            isStandardOutput: false);
        return boundProcess;
    }

    internal static bool TelegramGatewayConfigured(JsonElement status)
        => InspectTelegramGatewayStatus(status).Configured;

    internal static bool ShouldStartTelegramGateway(JsonElement status)
    {
        var inspected = InspectTelegramGatewayStatus(status);
        return inspected.Configured && !inspected.GatewayRunning;
    }

    internal static TelegramGatewayStatus InspectTelegramGatewayStatus(
        JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "Hermes returned malformed Telegram status.");

        var properties = status.EnumerateObject().ToArray();
        if (properties.Length != 3
            || properties.Select(static property => property.Name)
                .Distinct(StringComparer.Ordinal).Count() != 3
            || !status.TryGetProperty("configured", out var configured)
            || configured.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False)
            || !status.TryGetProperty("gateway_running", out var gatewayRunning)
            || gatewayRunning.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False)
            || !status.TryGetProperty("state", out var state)
            || state.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes returned malformed Telegram status.");
        }

        var isConfigured = configured.GetBoolean();
        var isGatewayRunning = gatewayRunning.GetBoolean();
        var stateValue = state.GetString();
        if (stateValue is not (
                "unconfigured" or "starting" or "connected" or "attention")
            || (!isConfigured
                && (isGatewayRunning || stateValue != "unconfigured"))
            || (isConfigured && stateValue == "unconfigured")
            || (stateValue == "connected" && !isGatewayRunning))
        {
            throw new InvalidDataException(
                "Hermes returned contradictory Telegram status.");
        }
        return new TelegramGatewayStatus(
            isConfigured,
            isGatewayRunning,
            stateValue);
    }

    internal readonly record struct TelegramGatewayStatus(
        bool Configured,
        bool GatewayRunning,
        string State);

    private async Task DrainOutputAsync(
        StreamReader reader,
        bool isStandardOutput)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is not null)
            {
                if (isStandardOutput)
                    Interlocked.Increment(ref _stdoutLineCount);
                else
                    Interlocked.Increment(ref _stderrLineCount);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    internal static void ConfigureNativeToolEnvironment(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Remove("HERMES_TUI_TOOLSETS");
        startInfo.Environment.Remove("HERMES_SAFE_MODE");
        startInfo.Environment.Remove("HERMES_CRON_SESSION");
        startInfo.Environment.Remove("HERMES_CRON_AUTO_DELIVER_PLATFORM");
        startInfo.Environment.Remove("HERMES_CRON_AUTO_DELIVER_CHAT_ID");
        startInfo.Environment.Remove("HERMES_CRON_AUTO_DELIVER_THREAD_ID");
        startInfo.Environment["HERMES_ENABLE_PROJECT_PLUGINS"] = "0";
        startInfo.Environment["HERMES_DESKTOP"] = "0";
        startInfo.Environment["HERMES_GATEWAY_SESSION"] = "1";
        startInfo.Environment["HERMES_CRON_TICKER"] = "1";
    }

    internal static void ConfigurePinnedIdentityEnvironment(
        ProcessStartInfo startInfo,
        LilithRuntimeOptions options,
        string expectedConfigSha256)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(options);
        if (!Path.IsPathFullyQualified(options.HermesHomePath))
        {
            throw new InvalidOperationException(
                "The strict identity profile home must be absolute.");
        }
        if (!IsSha256(expectedConfigSha256))
        {
            throw new InvalidOperationException(
                "The strict identity config digest is invalid.");
        }
        if (!startInfo.Environment.TryGetValue(
                "HERMES_HOME",
                out var providerBoundHome)
            || string.IsNullOrWhiteSpace(providerBoundHome)
            || !string.Equals(
                Path.GetFullPath(providerBoundHome!),
                Path.GetFullPath(options.HermesHomePath),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The provider lease did not bind the strict identity home.");
        }
        var expectedWorkspace = Path.GetFullPath(Path.Combine(
            options.HermesHomePath,
            "workspace"));
        LilithRuntimeWorkspacePathGuard.EnsureSafe(expectedWorkspace);
        if (string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            || !string.Equals(
                Path.GetFullPath(startInfo.WorkingDirectory),
                expectedWorkspace,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The strict identity workspace was not bound as the child cwd.");
        }
        startInfo.Environment.Remove("HERMES_EPHEMERAL_SYSTEM_PROMPT");
        startInfo.Environment.Remove("HERMES_ENVIRONMENT_HINT");
        startInfo.Environment["TERMINAL_CWD"] = expectedWorkspace;
        startInfo.Environment["HERMES_IDENTITY_REQUIRED"] = "1";
        startInfo.Environment["HERMES_IDENTITY_EXPECTED_FINGERPRINT"] =
            LilithRuntimeOptions.PinnedIdentityFingerprintSha256;
        startInfo.Environment["HERMES_IDENTITY_EXPECTED_CONFIG_SHA256"] =
            expectedConfigSha256.ToUpperInvariant();
        startInfo.Environment["HERMES_IDENTITY_EXPECTED_SOUL_SHA256"] =
            LilithIdentityPackageV9Loader.SoulAssetSha256;
        startInfo.Environment[
            "HERMES_IDENTITY_EXPECTED_PERSONALITY_SHA256"] =
            LilithIdentityPackageV9Loader.HermesPersonalityContentSha256;
        startInfo.Environment["HERMES_MANAGED_DIR"] =
            LilithRelationshipMemoryProvisioner.ManagedConfigDirectory(
                options.HermesHomePath);
        startInfo.Environment["HERMES_MANAGED_CONFIG_SHA256"] =
            LilithRelationshipMemoryProvisioner.ManagedConfigSha256;
    }

    internal static void ScrubInheritedIdentityEnvironment(
        ProcessStartInfo startInfo,
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(options);
        if (!Path.IsPathFullyQualified(options.HermesHomePath))
        {
            throw new InvalidOperationException(
                "The strict identity profile home must be absolute.");
        }

        foreach (var key in new[]
                 {
                     "HERMES_HOME",
                     "HERMES_EPHEMERAL_SYSTEM_PROMPT",
                     "HERMES_ENVIRONMENT_HINT",
                     "HERMES_ISO_CERTIFY_SYNTH_TURN",
                     "TERMINAL_CWD",
                     "HERMES_IDENTITY_REQUIRED",
                     "HERMES_IDENTITY_EXPECTED_FINGERPRINT",
                     "HERMES_IDENTITY_EXPECTED_CONFIG_SHA256",
                     "HERMES_IDENTITY_EXPECTED_SOUL_SHA256",
                      "HERMES_IDENTITY_EXPECTED_PERSONALITY_SHA256",
                      "HERMES_MANAGED_DIR",
                      "HERMES_MANAGED_CONFIG_SHA256"
                  })
        {
            startInfo.Environment.Remove(key);
        }
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;
        foreach (var character in value)
        {
            var hex = character is >= '0' and <= '9'
                or >= 'A' and <= 'F'
                or >= 'a' and <= 'f';
            if (!hex)
                return false;
        }
        return true;
    }

    internal static void ConfigurePinnedVoiceEnvironment(
        ProcessStartInfo startInfo,
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(options);
        if (!Path.IsPathFullyQualified(options.VoiceModelPath))
        {
            throw new InvalidOperationException(
                "The pinned local voice model path must be absolute.");
        }
        startInfo.Environment["HERMES_VOICE"] = "0";
        startInfo.Environment["HERMES_VOICE_TTS"] = "0";
        startInfo.Environment["HERMES_DISABLE_LAZY_INSTALLS"] = "1";
        // This child-only value is consumed exclusively by the strict game
        // capture route. StartAsync verifies the exact voice component closure
        // before reaching process creation, and assignment here replaces any
        // inherited hostile value.
        startInfo.Environment["HERMES_LILITH_GAME_STT_MODEL_PATH"] =
            Path.GetFullPath(options.VoiceModelPath);
    }

    internal static void ConfigurePortablePythonEnvironment(
        ProcessStartInfo startInfo,
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        foreach (var key in startInfo.Environment.Keys.ToArray())
        {
            if (key.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("VIRTUAL_ENV", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("CONDA", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("MAMBA", StringComparison.OrdinalIgnoreCase)
                || key.Equals("_CE_CONDA", StringComparison.OrdinalIgnoreCase)
                || key.Equals("_CE_M", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("PIP_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("UV_", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Environment.Remove(key);
            }
        }

        var source = Path.GetFullPath(options.HermesSourcePath);
        var dependencies = Path.GetFullPath(options.HermesSitePackagesPath);
        var voice = Path.GetFullPath(options.VoiceSitePackagesPath);
        var pythonPath = new[]
            {
                source,
                dependencies,
                Path.Combine(dependencies, "win32"),
                Path.Combine(dependencies, "win32", "lib"),
                Path.Combine(dependencies, "Pythonwin"),
                voice
            }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pythonPath.Length != 6
            || pythonPath.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "The packaged Python import path is invalid or ambiguous.");
        }

        startInfo.Environment["PYTHONPATH"] = string.Join(
            Path.PathSeparator,
            pythonPath);
        startInfo.Environment["PYTHONNOUSERSITE"] = "1";
        startInfo.Environment["PYTHONSAFEPATH"] = "1";
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        startInfo.Environment["HERMES_PYTHON_SRC_ROOT"] = source;
        startInfo.Environment["HERMES_DESKTOP_HERMES_ROOT"] = source;
        startInfo.Environment["HERMES_BUNDLED_SKILLS"] =
            Path.Combine(source, "skills");
        startInfo.Environment["HERMES_BUNDLED_LOCALES"] =
            Path.Combine(source, "locales");
        startInfo.Environment["HERMES_BUNDLED_PLUGINS"] =
            Path.Combine(source, "plugins");
        startInfo.Environment["HERMES_OPTIONAL_SKILLS"] =
            Path.Combine(source, "optional-skills");
        startInfo.Environment["HERMES_OPTIONAL_MCPS"] =
            Path.Combine(source, "optional-mcps");
        startInfo.Environment["HERMES_NODE_SKIP_LINKS"] = "1";
        HermesManagedCliEnvironment.Configure(startInfo, options);
    }

    internal static void ConfigurePortablePythonEntrypoint(
        ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.ArgumentList.Count != 0)
        {
            throw new InvalidOperationException(
                "The packaged Python entry point must be the first argument surface.");
        }
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add("-B");
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("hermes_cli.main");
    }

    internal static void ConfigurePinnedCheckpointEnvironment(
        ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment["HERMES_TUI_CHECKPOINTS"] = "1";
        startInfo.Environment["HERMES_CHECKPOINT_TIMEOUT"] = "30";
    }

    private async Task<HermesStopReceipt> StopCoreAsync(
        bool keepFaultedState,
        bool terminateImmediately,
        CancellationToken cancellationToken)
    {
        var originalState = State;
        if (State != LilithRuntimeState.Faulted)
        {
            State = LilithRuntimeState.Stopping;
        }
        if (_generationId != Guid.Empty)
        {
            using var modelAuthority = await _modelAuthority.AcquireAsync(
                    _generationId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            _modelAuthority.Retire(modelAuthority, _generationId);
        }
        // Stop new microphone admission before detaching the still-live
        // gateway. Keep the existing owner bound until ACTIVE_PROCESS_ZERO is
        // proven; clearing it here would let another surface claim and send a
        // start request into the generation being retired.
        _voiceCaptureArbiter.BeginGenerationRetirement();

        var gateway = _gateway;
        _gateway = null;
        var gatewayCloseAttempted = gateway is not null;
        if (gateway is not null)
            gateway.EventReceived -= ForwardGatewayEvent;
        if (!terminateImmediately && gateway is not null)
        {
            await CloseAndDisposeGatewayAsync(gateway, cancellationToken)
                .ConfigureAwait(false);
        }

        var forcedTermination = false;
        var processExited = true;
        Exception? containmentFailure = null;
        if (_messagingJobProcess is not null)
        {
            var boundMessagingProcess = _messagingJobProcess;
            try
            {
                if (boundMessagingProcess.Job.ActiveProcessCount != 0)
                {
                    forcedTermination = true;
                    await boundMessagingProcess.Job.TerminateAndWaitAsync(
                            exitCode: 1,
                            timeout: TimeSpan.FromSeconds(10),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await boundMessagingProcess.Process
                    .WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
                processExited = boundMessagingProcess.Process.HasExited
                    && boundMessagingProcess.Job.ActiveProcessCount == 0;
            }
            catch (Exception exception)
            {
                containmentFailure = exception;
                processExited = false;
            }
            finally
            {
                try
                {
                    await CompleteMessagingOutputDrainAsync().ConfigureAwait(false);
                }
                finally
                {
                    boundMessagingProcess.Dispose();
                    _messagingJobProcess = null;
                }
            }
        }
        if (_jobProcess is not null)
        {
            var boundProcess = _jobProcess;
            try
            {
                if (!terminateImmediately && !boundProcess.Process.HasExited)
                {
                    try
                    {
                        await boundProcess.Process.WaitForExitAsync(
                                CancellationToken.None)
                            .WaitAsync(_options.ShutdownGracePeriod)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                if (boundProcess.Job.ActiveProcessCount != 0)
                {
                    forcedTermination = true;
                    await boundProcess.Job.TerminateAndWaitAsync(
                            exitCode: 1,
                            timeout: TimeSpan.FromSeconds(10),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                await boundProcess.Process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
                processExited = processExited
                    && boundProcess.Process.HasExited
                    && boundProcess.Job.ActiveProcessCount == 0;
            }
            catch (Exception exception)
            {
                containmentFailure ??= exception;
                processExited = false;
            }
            finally
            {
                try
                {
                    await CompleteOutputDrainAsync().ConfigureAwait(false);
                }
                finally
                {
                    boundProcess.Dispose();
                    _jobProcess = null;
                }
            }
        }

        if (terminateImmediately && gateway is not null)
        {
            // Credential replacement/removal changes process-global Hermes
            // state. Once its canonical response has been flushed, close the
            // entire old Job before spending any time on graceful WebSocket
            // cleanup, then retire the disconnected client object boundedly.
            using var gatewayCleanup = new CancellationTokenSource(
                TimeSpan.FromSeconds(2));
            await CloseAndDisposeGatewayAsync(gateway, gatewayCleanup.Token)
                .ConfigureAwait(false);
        }

        var readyRemoved = DeleteExactFile(_readyFilePath);
        _readyFilePath = null;
        _port = 0;

        if (containmentFailure is not null)
        {
            State = LilithRuntimeState.Faulted;
            throw new LilithRuntimeException(
                "The Hermes process job did not terminate and reach ACTIVE_PROCESS_ZERO.");
        }
        if (!processExited)
        {
            State = LilithRuntimeState.Faulted;
            throw new LilithRuntimeException(
                "The Hermes process job did not reach ACTIVE_PROCESS_ZERO.");
        }
        // Only physical process-tree retirement revokes the old capture
        // identity. The retired gate remains closed until StartAsync assigns a
        // fresh generation and calls Reset.
        _voiceCaptureArbiter.CompleteGenerationRetirement();
        State = keepFaultedState || originalState == LilithRuntimeState.Faulted
            ? LilithRuntimeState.Faulted
            : LilithRuntimeState.Stopped;
        return new HermesStopReceipt(
            _generationId,
            gatewayCloseAttempted,
            processExited,
            forcedTermination,
            readyRemoved);
    }

    private static async Task CloseAndDisposeGatewayAsync(
        HermesGatewayClient gateway,
        CancellationToken cancellationToken)
    {
        try
        {
            await gateway.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }

        await gateway.DisposeAsync().ConfigureAwait(false);
    }

    private LilithRuntimeSnapshot Snapshot()
    {
        if (_jobProcess is null || _profile is null || _port <= 0)
        {
            throw new LilithRuntimeException(
                "Lilith Runtime does not have a complete ready snapshot.");
        }

        var model = EffectiveModelIdentity();
        return new LilithRuntimeSnapshot(
            _generationId,
            State,
            _jobProcess.Process.Id,
            _port,
            LilithRuntimeOptions.PinnedHermesCommit,
            _options.HermesHomePath,
            model.ProviderId,
            model.ModelId,
            _providerInstanceId,
            _providerConfigurationGeneration,
            GatewayReady: true,
            JobObjectBound: true,
            _profile);
    }

    private async Task CompleteOutputDrainAsync()
    {
        var stdout = _stdoutDrainTask;
        var stderr = _stderrDrainTask;
        _stdoutDrainTask = null;
        _stderrDrainTask = null;
        if (stdout is null && stderr is null)
            return;
        try
        {
            await Task.WhenAll(
                    stdout ?? Task.CompletedTask,
                    stderr ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception)
        {
            // Output is diagnostic-only. Process-tree containment and bounded
            // disposal remain authoritative even if a reader itself faults.
        }
    }

    private async Task CompleteMessagingOutputDrainAsync()
    {
        var stdout = _messagingStdoutDrainTask;
        var stderr = _messagingStderrDrainTask;
        _messagingStdoutDrainTask = null;
        _messagingStderrDrainTask = null;
        if (stdout is null && stderr is null)
            return;
        try
        {
            await Task.WhenAll(
                    stdout ?? Task.CompletedTask,
                    stderr ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception)
        {
            // Messaging output is counted only; no platform or secret-bearing
            // line crosses the Runtime boundary.
        }
    }

    private HermesGatewayClient RequireReadyGateway()
    {
        if (State != LilithRuntimeState.Ready || _gateway is null)
        {
            throw new LilithRuntimeException("Lilith Runtime is not ready.");
        }

        return _gateway;
    }

    private void ForwardGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        var handlers = GatewayEvent;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<HermesGatewayEvent> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(gatewayEvent);
            }
            catch
            {
            }
        }
    }

    internal bool ApplyAttestedNativeModelDefault(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId,
        LilithRuntimeApprovedModelCatalog catalog,
        string providerId,
        string modelId)
    {
        return State == LilithRuntimeState.Ready
            && _modelAuthority.ApplyAttestedDefault(
                lease,
                generationId,
                catalog,
                providerId,
                modelId);
    }

    internal static bool IsExactNativeGlobalModelReceipt(
        JsonElement receipt,
        string expectedModelId)
        => HasExactProperties(
                receipt,
                "key",
                "value",
                "warning",
                "confirm_required",
                "confirm_message",
                "scope",
                "runtime_revision",
                "runtime_ready")
            && receipt.TryGetProperty("key", out var key)
            && key.ValueKind == JsonValueKind.String
            && string.Equals(key.GetString(), "model", StringComparison.Ordinal)
            && receipt.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(
                value.GetString(),
                expectedModelId,
                StringComparison.Ordinal)
            && receipt.TryGetProperty("confirm_required", out var confirmation)
            && confirmation.ValueKind is JsonValueKind.False
            && receipt.TryGetProperty("warning", out var warning)
            && warning.ValueKind == JsonValueKind.String
            && receipt.TryGetProperty("confirm_message", out var confirmMessage)
            && confirmMessage.ValueKind == JsonValueKind.String
            && receipt.TryGetProperty("scope", out var scope)
            && scope.ValueKind == JsonValueKind.String
            && string.Equals(scope.GetString(), "global", StringComparison.Ordinal)
            && receipt.TryGetProperty("runtime_revision", out var runtimeRevision)
            && runtimeRevision.ValueKind == JsonValueKind.Number
            && runtimeRevision.TryGetInt64(out var revision)
            && revision > 0
            && receipt.TryGetProperty("runtime_ready", out var runtimeReady)
            && runtimeReady.ValueKind == JsonValueKind.True;

    private static bool HasExactProperties(
        JsonElement value,
        params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var observed = value.EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
        return observed.Length == expected.Length
            && observed.ToHashSet(StringComparer.Ordinal)
                .SetEquals(expected);
    }

    internal static async Task<int> WaitForReadyPortAsync(
        Process process,
        string readyFilePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new LilithRuntimeException(
                    "Hermes exited before publishing its ready sentinel.",
                    LilithRuntimeFailureKind.GatewayStartupFailed);
            }

            if (File.Exists(readyFilePath))
            {
                try
                {
                    await using var stream = new FileStream(
                        readyFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var document = await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (document.RootElement.TryGetProperty("port", out var portElement)
                        && portElement.TryGetInt32(out var port)
                        && port is > 0 and <= 65535)
                    {
                        return port;
                    }
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new LilithRuntimeException(
            "Hermes did not publish a valid ready sentinel in time.",
            LilithRuntimeFailureKind.GatewayReadyTimeout);
    }

    private static string CreateSessionToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new LilithRuntimeException(
                $"Hermes response is missing required '{property}'.");
        }

        return value.GetString()!;
    }

    private static bool IsEmptyArrayProperty(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() == 0;

    private static int ArrayLength(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static IReadOnlyList<string> ObjectPropertyNames(
        JsonElement element,
        string property,
        int maximum)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Object
                ? value.EnumerateObject()
                    .Take(maximum)
                    .Select(item => item.Name)
                    .ToArray()
                : Array.Empty<string>();

    private static IReadOnlyList<string> ObjectArrayStringValues(
        JsonElement element,
        string property,
        int maximum)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var entry in value.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var item in entry.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;
                result.Add(item.GetString()!);
                if (result.Count >= maximum)
                    return result;
            }
        }

        return result;
    }

    private static bool DeleteExactFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
