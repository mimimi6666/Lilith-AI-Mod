using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace LilithVoiceHost;

internal static class Program
{
    private const int StartupReadinessTimeoutMs = 90_000;
    private const int StartupReadinessPollIntervalMs = 500;
    private const int PortProbeTimeoutMs = 500;
    private const int RuntimeHealthPollIntervalMs = 2_000;
    private const int RuntimePortFailureLimit = 3;
    private const int ShutdownProcessWaitTimeoutMs = 5_000;
    private const int LogQueueCapacity = 1_024;

    private static async Task<int> Main(string[] args)
    {
        var parentPid = 0;
        var index = Array.FindIndex(args, value => string.Equals(value, "--parent", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length)
            int.TryParse(args[index + 1], out parentPid);
        var language = GetArgument(args, "--language");
        var useJapanese = string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "jp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "japanese", StringComparison.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, "Local\\LilithAIVoiceHost", out var created);
        if (!created) return 0;

        var root = AppContext.BaseDirectory;
        var logDirectory = Path.Combine(root, "logs");
        TryCreateDirectory(logDirectory);
        var log = Path.Combine(logDirectory, "voice-host.log");
        var logSink = new VoiceHostLogSink(log, LogQueueCapacity);
        var owned = new List<OwnedService>();
        var exitCode = 0;
        using var parentProcess = TryCaptureParentProcess(parentPid);
        try
        {
            if (parentPid > 0 && parentProcess == null)
            {
                await logSink.WriteAsync(
                    $"Parent process {parentPid} was unavailable before voice startup.");
                return 0;
            }

            var python = Path.Combine(root, "python", "Scripts", "python.exe");
            var api = Path.Combine(root, "gpt-sovits", "api_v2.py");
            if (!File.Exists(python) || !File.Exists(api) || !File.Exists(Path.Combine(root, ".ready")))
            {
                await logSink.WriteAsync("Voice runtime is not ready.");
                return 1;
            }
            var device = File.Exists(Path.Combine(root, "device.txt"))
                ? File.ReadAllText(Path.Combine(root, "device.txt")).Trim().ToLowerInvariant()
                : "auto";
            if (device is not ("auto" or "cuda" or "cpu"))
                throw new InvalidOperationException("Unsupported voice device preference.");
            var services = useJapanese
                ? new[] { (Port: 9881, Name: "ja") }
                : new[] { (Port: 9880, Name: "zh") };
            foreach (var service in services)
            {
                if (await PortOpenAsync(service.Port, 300))
                {
                    await logSink.WriteAsync($"Existing voice endpoint '{service.Name}' was not started by this host; its execution device is not verified here.");
                    continue;
                }
                var config = Path.Combine(root, "config", $"{service.Name}-{(device == "cpu" ? "cpu" : "cuda")}.yaml");
                if (!File.Exists(config)) throw new FileNotFoundException("Voice configuration is missing.", config);
                var process = Process.Start(VoiceHostPythonProcessStartInfo.Create(
                    python,
                    api,
                    service.Port,
                    config,
                    Path.Combine(root, "gpt-sovits"),
                    device))
                    ?? throw new InvalidOperationException("Could not start the voice service.");
                var ownedService = new OwnedService(service.Port, service.Name, process);
                owned.Add(ownedService);
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        logSink.TryWrite($"[{service.Name}] {e.Data}");
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        logSink.TryWrite($"[{service.Name}:err] {e.Data}");
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            var readiness = await WaitForReadinessAsync(
                logSink,
                parentPid,
                parentProcess,
                services,
                owned);
            if (readiness == StartupReadiness.ParentExited)
                return 0;
            if (readiness != StartupReadiness.Ready)
                return 2;

            await logSink.WriteAsync(
                $"Voice host started (requested-device={device}, language={(useJapanese ? "ja" : "zh")}); owned processes={owned.Count}; only owned children emit verified LILITH_VOICE_DEVICE/MODELS receipts.");
            exitCode = await MonitorRuntimeAsync(
                logSink,
                parentPid,
                parentProcess,
                services,
                owned);
        }
        catch (Exception exception)
        {
            await logSink.WriteAsync(exception.ToString());
            exitCode = 1;
        }
        finally
        {
            await StopOwnedServicesAsync(logSink, owned);
            await logSink.WriteAsync("Voice host stopped.");
            await logSink.CompleteAndDrainAsync();
            foreach (var service in owned)
            {
                try { service.Process.Dispose(); } catch { }
            }
        }

        return exitCode;
    }

    private static async Task<StartupReadiness> WaitForReadinessAsync(
        VoiceHostLogSink log,
        int parentPid,
        ParentProcessLease? parentProcess,
        IReadOnlyList<(int Port, string Name)> services,
        IReadOnlyList<OwnedService> owned)
    {
        var stopwatch = Stopwatch.StartNew();
        var unavailablePorts = new List<int>();
        while (stopwatch.ElapsedMilliseconds < StartupReadinessTimeoutMs)
        {
            if (parentPid > 0 && !IsParentProcessRunning(parentProcess))
            {
                await log.WriteAsync(
                    $"Parent process {parentPid} exited before voice readiness.");
                return StartupReadiness.ParentExited;
            }

            foreach (var service in owned)
            {
                if (!service.Process.HasExited) continue;
                await log.WriteAsync(
                    $"Voice service '{service.Name}' exited before readiness with code {SafeExitCode(service.Process)}.");
                return StartupReadiness.ChildExited;
            }

            unavailablePorts.Clear();
            foreach (var service in services)
            {
                if (!await PortOpenAsync(service.Port, PortProbeTimeoutMs))
                    unavailablePorts.Add(service.Port);
            }

            if (unavailablePorts.Count == 0)
                return StartupReadiness.Ready;

            var remainingMs = StartupReadinessTimeoutMs - stopwatch.ElapsedMilliseconds;
            if (remainingMs <= 0) break;
            await Task.Delay((int)Math.Min(StartupReadinessPollIntervalMs, remainingMs));
        }

        foreach (var service in owned)
        {
            if (!service.Process.HasExited) continue;
            await log.WriteAsync(
                $"Voice service '{service.Name}' exited before readiness with code {SafeExitCode(service.Process)}.");
            return StartupReadiness.ChildExited;
        }

        await log.WriteAsync(
            $"Voice service readiness timed out after {StartupReadinessTimeoutMs} ms; unavailable ports={string.Join(",", unavailablePorts)}.");
        return StartupReadiness.TimedOut;
    }

    private static async Task<int> MonitorRuntimeAsync(
        VoiceHostLogSink log,
        int parentPid,
        ParentProcessLease? parentProcess,
        IReadOnlyList<(int Port, string Name)> services,
        IReadOnlyList<OwnedService> owned)
    {
        if (parentPid <= 0) return 0;

        var consecutivePortFailures = services.ToDictionary(service => service.Port, _ => 0);
        while (true)
        {
            if (!IsParentProcessRunning(parentProcess))
            {
                await log.WriteAsync(
                    $"Parent process {parentPid} exited; stopping voice host.");
                return 0;
            }

            foreach (var service in owned)
            {
                if (!service.Process.HasExited) continue;
                await log.WriteAsync(
                    $"Voice service '{service.Name}' exited while running with code {SafeExitCode(service.Process)}; stopping voice host.");
                return 3;
            }

            foreach (var service in services)
            {
                if (await PortOpenAsync(service.Port, PortProbeTimeoutMs))
                {
                    if (consecutivePortFailures[service.Port] > 0)
                    {
                        await log.WriteAsync(
                            $"Voice service '{service.Name}' port {service.Port} recovered after {consecutivePortFailures[service.Port]} failed probe(s).");
                    }
                    consecutivePortFailures[service.Port] = 0;
                    continue;
                }

                var failures = ++consecutivePortFailures[service.Port];
                if (failures == 1)
                {
                    await log.WriteAsync(
                        $"Voice service '{service.Name}' port {service.Port} health probe failed (1/{RuntimePortFailureLimit}).");
                }
                if (failures < RuntimePortFailureLimit) continue;

                await log.WriteAsync(
                    $"Voice service '{service.Name}' port {service.Port} remained unavailable for {failures} consecutive probes; stopping voice host.");
                return 4;
            }

            await Task.Delay(RuntimeHealthPollIntervalMs);
        }
    }

    private static ParentProcessLease? TryCaptureParentProcess(int processId)
    {
        if (processId <= 0) return null;

        try
        {
            var process = Process.GetProcessById(processId);
            try
            {
                return new ParentProcessLease(
                    process,
                    process.StartTime.ToUniversalTime().Ticks);
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool IsParentProcessRunning(ParentProcessLease? parentProcess)
    {
        if (parentProcess == null) return false;

        try
        {
            return !parentProcess.Process.HasExited
                && parentProcess.Process.StartTime.ToUniversalTime().Ticks
                == parentProcess.StartTimeUtcTicks;
        }
        catch
        {
            return false;
        }
    }

    private static async Task StopOwnedServicesAsync(
        VoiceHostLogSink log,
        IReadOnlyList<OwnedService> owned)
    {
        foreach (var service in owned)
        {
            try
            {
                if (!service.Process.HasExited)
                    service.Process.Kill(true);
            }
            catch (Exception exception)
            {
                await log.WriteAsync(
                    $"Voice service '{service.Name}' could not be stopped: {exception.Message}");
            }
        }

        foreach (var service in owned)
        {
            try
            {
                using var timeout = new CancellationTokenSource(
                    ShutdownProcessWaitTimeoutMs);
                await service.Process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                await log.WriteAsync(
                    $"Voice service '{service.Name}' did not exit within {ShutdownProcessWaitTimeoutMs} ms after shutdown.");
            }
            catch (Exception exception)
            {
                await log.WriteAsync(
                    $"Voice service '{service.Name}' exit could not be observed: {exception.Message}");
            }
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    private static string GetArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1].Trim() : string.Empty;
    }

    private static async Task<bool> PortOpenAsync(int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch { return false; }
    }

    private static void TryCreateDirectory(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
    }

    private sealed record OwnedService(int Port, string Name, Process Process);

    private sealed record ParentProcessLease(
        Process Process,
        long StartTimeUtcTicks) : IDisposable
    {
        public void Dispose()
        {
            Process.Dispose();
        }
    }

    private readonly record struct VoiceHostLogEntry(
        DateTimeOffset Timestamp,
        string Message);

    private sealed class VoiceHostLogSink
    {
        private readonly string _path;
        private readonly Channel<VoiceHostLogEntry> _channel;
        private readonly Task _drainTask;
        private long _droppedEntryCount;

        internal VoiceHostLogSink(string path, int capacity)
        {
            _path = path;
            _channel = Channel.CreateBounded<VoiceHostLogEntry>(
                new BoundedChannelOptions(capacity)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });
            _drainTask = DrainAsync();
        }

        internal async ValueTask WriteAsync(string message)
        {
            try
            {
                await _channel.Writer.WriteAsync(
                    new VoiceHostLogEntry(DateTimeOffset.Now, message));
            }
            catch
            {
                // Logging must never change voice-host lifecycle truth.
            }
        }

        internal void TryWrite(string message)
        {
            if (!_channel.Writer.TryWrite(
                    new VoiceHostLogEntry(DateTimeOffset.Now, message)))
            {
                Interlocked.Increment(ref _droppedEntryCount);
            }
        }

        internal async Task CompleteAndDrainAsync()
        {
            _channel.Writer.TryComplete();
            try { await _drainTask; } catch { }
        }

        private async Task DrainAsync()
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync())
                await AppendBestEffortAsync(entry);

            var dropped = Interlocked.Exchange(ref _droppedEntryCount, 0);
            if (dropped > 0)
            {
                await AppendBestEffortAsync(
                    new VoiceHostLogEntry(
                        DateTimeOffset.Now,
                        $"Voice host log queue dropped {dropped} child output line(s)."));
            }
        }

        private async Task AppendBestEffortAsync(VoiceHostLogEntry entry)
        {
            try
            {
                await File.AppendAllTextAsync(
                    _path,
                    $"{entry.Timestamp:O} {entry.Message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch
            {
                // The host remains truthful even when its diagnostic sink is unavailable.
            }
        }
    }

    private enum StartupReadiness
    {
        Ready,
        ParentExited,
        ChildExited,
        TimedOut
    }
}
