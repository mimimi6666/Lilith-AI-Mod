using System.Security.Principal;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal interface ILilithRuntimeServeRuntime : IAsyncDisposable
{
    Task<LilithRuntimeSnapshot> StartAsync(
        CancellationToken cancellationToken = default);

    Task<HermesNativeSurfaceProbeReceipt> ProbeNativeSurfaceAsync(
        CancellationToken cancellationToken = default);

    Task<HermesStopReceipt> StopAsync(
        CancellationToken cancellationToken = default);

    Task<HermesStopReceipt> StopCredentialMutationGenerationAsync(
        CancellationToken cancellationToken = default);

    Task<HermesStopReceipt> StopVoiceRecoveryGenerationAsync(
        CancellationToken cancellationToken = default);
}

internal interface ILilithRuntimePipeRunner : IAsyncDisposable
{
    Task<LilithRuntimePipeRunDisposition> RunAsync(
        CancellationToken cancellationToken = default);
}

public enum LilithRuntimePipeRunDisposition
{
    Shutdown = 0,
    CredentialMutationRestartRequired = 1,
    VoiceRecoveryRestartRequired = 2
}

internal interface ILilithRuntimeSingleInstanceLease : IDisposable
{
}

internal enum LilithRuntimeServeExit
{
    Stopped = 0,
    AlreadyRunning = 3
}

internal sealed class LilithRuntimeServeHost
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<ILilithRuntimeSingleInstanceLease?>
        _acquireSingleInstance;
    private readonly Func<ILilithRuntimeServeRuntime> _createRuntime;
    private readonly Func<CancellationToken, Task<bool>> _preparePlayerData;
    private readonly Func<
        ILilithRuntimeServeRuntime,
        LilithRuntimeSnapshot,
        ILilithRuntimePipeRunner> _createPipe;

    internal LilithRuntimeServeHost()
        : this(
            LilithRuntimeSingleInstanceLease.TryAcquireCurrentUser,
            () => new LilithRuntimeService(),
            (runtime, snapshot) => new LilithRuntimeCompositePipeRunner(
                new LilithRuntimePipeServer(
                    (LilithRuntimeService)runtime,
                    snapshot),
                new LilithGameBridgePipeServer(
                    (LilithRuntimeService)runtime,
                    snapshot)),
            LilithPlayerSaveCommand.RecoverBeforeServeAsync)
    {
    }

    internal LilithRuntimeServeHost(
        Func<ILilithRuntimeSingleInstanceLease?> acquireSingleInstance,
        Func<ILilithRuntimeServeRuntime> createRuntime,
        Func<
            ILilithRuntimeServeRuntime,
            LilithRuntimeSnapshot,
            ILilithRuntimePipeRunner> createPipe,
        Func<CancellationToken, Task<bool>>? preparePlayerData = null)
    {
        _acquireSingleInstance = acquireSingleInstance
            ?? throw new ArgumentNullException(nameof(acquireSingleInstance));
        _createRuntime = createRuntime
            ?? throw new ArgumentNullException(nameof(createRuntime));
        _createPipe = createPipe
            ?? throw new ArgumentNullException(nameof(createPipe));
        _preparePlayerData = preparePlayerData ?? (_ => Task.FromResult(true));
    }

    internal async Task<LilithRuntimeServeExit> RunAsync(
        CancellationToken cancellationToken)
    {
        using var instance = _acquireSingleInstance();
        if (instance is null)
            return LilithRuntimeServeExit.AlreadyRunning;

        // Older installed Runtime processes only hold the per-user semaphore.
        // Recovery is a writer: never run it before this lease is acquired.
        if (!await _preparePlayerData(cancellationToken).ConfigureAwait(false))
            throw new LilithRuntimeException("Player data recovery did not complete.");

        var runtime = _createRuntime();
        var normalStop = false;
        var previousGeneration = Guid.Empty;
        try
        {
            while (true)
            {
                var snapshot = await runtime.StartAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot.GenerationId == previousGeneration)
                {
                    throw new LilithRuntimeException(
                        "Lilith Runtime reused a retired generation identity.");
                }
                try
                {
                    var probe = await runtime.ProbeNativeSurfaceAsync(cancellationToken)
                        .ConfigureAwait(false);
                    ValidateProbe(snapshot, probe);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw LilithRuntimeStartupFailure.Classify(exception,
                        LilithRuntimeFailureKind.NativeSurfaceVerificationFailed);
                }
                previousGeneration = snapshot.GenerationId;

                LilithRuntimePipeRunDisposition disposition;
                await using (var pipe = _createPipe(runtime, snapshot))
                {
                    disposition = await pipe.RunAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    normalStop = true;
                    return LilithRuntimeServeExit.Stopped;
                }
                if (disposition
                    is not (
                        LilithRuntimePipeRunDisposition
                            .CredentialMutationRestartRequired
                        or LilithRuntimePipeRunDisposition
                            .VoiceRecoveryRestartRequired))
                {
                    throw new LilithRuntimeException(
                        "Lilith Runtime pipe server stopped unexpectedly.");
                }

                if (disposition
                    == LilithRuntimePipeRunDisposition
                        .CredentialMutationRestartRequired)
                {
                    await StopGenerationAsync(runtime).ConfigureAwait(false);
                }
                // A voice-recovery disposition is emitted only after the game
                // Host has synchronously retired the exact old Hermes Job and
                // removed its ready sentinel. Calling a second, differently
                // named stop here would weaken that response-before-release
                // proof; proceed directly to fresh startup and attestation.
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            normalStop = true;
            return LilithRuntimeServeExit.Stopped;
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(ShutdownTimeout);
            try
            {
                var receipt = await runtime.StopAsync(shutdown.Token)
                    .ConfigureAwait(false);
                if (normalStop
                    && (!receipt.ProcessExited || !receipt.ReadySentinelRemoved))
                {
                    throw new LilithRuntimeException(
                        "Lilith Runtime did not complete bounded shutdown.");
                }
            }
            catch when (!normalStop)
            {
                // Preserve the startup/probe/pipe failure while still making
                // the required bounded stop attempt.
            }
            finally
            {
                try
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                catch when (!normalStop)
                {
                    // Preserve the failure that caused shutdown while still
                    // making the required disposal attempt.
                }
            }
        }
    }

    private static async Task StopGenerationAsync(
        ILilithRuntimeServeRuntime runtime)
    {
        using var shutdown = new CancellationTokenSource(ShutdownTimeout);
        var receipt = await runtime.StopCredentialMutationGenerationAsync(
                shutdown.Token)
            .ConfigureAwait(false);
        if (!receipt.ProcessExited || !receipt.ReadySentinelRemoved)
        {
            throw new LilithRuntimeException(
                "Lilith Runtime did not complete credential generation rollover.");
        }
    }

    private static void ValidateProbe(
        LilithRuntimeSnapshot snapshot,
        HermesNativeSurfaceProbeReceipt probe)
    {
        if (probe.GenerationId != snapshot.GenerationId
            || !probe.CredentialStatusReceived
            || !probe.CatalogSurfaceValid
            || probe.CommandCount <= 0
            || probe.ModelProviderCount <= 0
            || probe.NativeCatalogToolsetCount <= 0
            || probe.NativeApprovalMode is not ("manual" or "smart" or "off")
            || !probe.NoPromptSubmitted
            || !string.Equals(
                probe.SessionId,
                string.Empty,
                StringComparison.Ordinal)
            || probe.SessionStatusReceived
            || probe.SessionNativeSurfaceValid
            || probe.NativeToolCount != 0
            || probe.NativeToolsetCount != 0
            || probe.McpServerCount != 0)
        {
            throw new LilithRuntimeException(
                "Lilith Runtime native-surface startup attestation was rejected.",
                LilithRuntimeFailureKind.NativeSurfaceVerificationFailed);
        }
    }
}

internal sealed class LilithRuntimeSingleInstanceLease :
    ILilithRuntimeSingleInstanceLease
{
    private const string NamePrefix = @"Local\LilithRuntime.Serve.v1.";

    private readonly Semaphore _semaphore;
    private bool _held;

    private LilithRuntimeSingleInstanceLease(Semaphore semaphore)
    {
        _semaphore = semaphore;
        _held = true;
    }

    internal static ILilithRuntimeSingleInstanceLease? TryAcquireCurrentUser()
        => TryAcquire(BuildCurrentUserName());

    internal static ILilithRuntimeSingleInstanceLease? TryAcquire(string name)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Lilith Runtime serve requires Windows.");
        if (string.IsNullOrWhiteSpace(name)
            || !name.StartsWith(@"Local\", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A Local Windows semaphore name is required.",
                nameof(name));
        }

        Semaphore? semaphore = null;
        try
        {
            semaphore = new Semaphore(
                initialCount: 1,
                maximumCount: 1,
                name,
                out _);
            if (!semaphore.WaitOne(0))
            {
                semaphore.Dispose();
                return null;
            }

            return new LilithRuntimeSingleInstanceLease(semaphore);
        }
        catch
        {
            semaphore?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (!_held)
            return;
        _held = false;
        try
        {
            _semaphore.Release();
        }
        finally
        {
            _semaphore.Dispose();
        }
    }

    private static string BuildCurrentUserName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            throw new LilithRuntimeException(
                "The current Windows user identity is unavailable.");
        return NamePrefix + sid;
    }
}

internal sealed class LilithRuntimeShutdownSignals : IDisposable
{
    private readonly EventWaitHandle? _desktopExit;
    private readonly RegisteredWaitHandle? _desktopExitWait;
    private readonly CancellationTokenSource _source;
    private readonly ConsoleCancelEventHandler _consoleHandler;
    private readonly EventHandler _processExitHandler;
    private bool _disposed;

    internal CancellationToken Token => _source.Token;

    internal LilithRuntimeShutdownSignals(CancellationToken cancellationToken = default, bool enableDesktopExit = false)
    {
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _consoleHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestStop();
        };
        _processExitHandler = (_, _) => RequestStop();
        Console.CancelKeyPress += _consoleHandler;
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        if (enableDesktopExit && OperatingSystem.IsWindows())
        {
            var name = LilithRuntimeContracts.LilithDesktopLifecycle.SignalName("runtime-exit",
                System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, Environment.ProcessPath!);
            // Manual-reset broadcasts to short-lived duplicate bootstrap processes too;
            // opening an existing event must never clear an in-progress exit request.
            _desktopExit = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            _desktopExitWait = ThreadPool.RegisterWaitForSingleObject(_desktopExit,
                (_, _) => RequestStop(), null, Timeout.Infinite, executeOnlyOnce: true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Console.CancelKeyPress -= _consoleHandler;
        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        _desktopExitWait?.Unregister(null);
        _desktopExit?.Dispose();
        _source.Dispose();
    }

    private void RequestStop()
    {
        if (_disposed)
            return;
        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
