using System.Text.Json;
using LilithRuntime;

return await LilithRuntimeProgram.RunAsync(args).ConfigureAwait(false);

internal static class LilithRuntimeProgram
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0
            || arguments[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("LilithRuntime serve | smoke [--compact] | data-export --archive <path> | data-import --archive <path> | identity-export --packages-root <path> --runtime-root <path>");
            return 0;
        }

        if (arguments[0] is "identity-export" or "identity-export-v3" or "identity-export-v4" or "identity-export-v5" or "identity-export-v6" or "identity-export-v7" or "identity-export-v8" or "identity-export-v9")
            return LilithIdentityTransitionExport.Run(arguments);

        if (arguments[0] is "data-export" or "data-import")
            return await LilithPlayerSaveCommand.RunAsync(arguments).ConfigureAwait(false);

        if (string.Equals(arguments[0], "serve", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Length != 1)
            {
                Console.Error.WriteLine("The serve command accepts no options.");
                return 2;
            }

            return await RunServeAsync().ConfigureAwait(false);
        }

        if (!string.Equals(arguments[0], "smoke", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Unknown command. Use: LilithRuntime serve or LilithRuntime smoke");
            return 2;
        }

        if (!TryParseSmokeOptions(
                arguments.AsSpan(1),
                out var compact,
                out var smokeProfileRoot))
        {
            Console.Error.WriteLine(
                "Use: LilithRuntime smoke [--compact] [--profile-root <new-temp-path>]");
            return 2;
        }
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = !compact
        };

        var runtimeOptions = smokeProfileRoot is null
            ? LilithRuntimeOptions.CreateDefault()
            : LilithRuntimeOptions.CreateDefault() with
            {
                HermesHomePath = smokeProfileRoot
            };
        try
        {
            LilithPreviousConfigRecovery.ThrowIfPending(runtimeOptions.HermesHomePath);
        }
        catch (LilithRuntimeException exception)
        {
            return ReportStartupFailure(exception, Console.Error);
        }
        using var smokeLease = LilithPlayerSaveServiceLease.TryAcquire(runtimeOptions.HermesHomePath,
            dedicatedDefaultProfile: smokeProfileRoot is null);
        if (smokeLease is null)
        {
            Console.Error.WriteLine("Lilith player data is in use; smoke was not started.");
            return LilithRuntimeContracts.LilithRuntimeProcessExit.AlreadyRunning;
        }
        if (smokeProfileRoot is null
            && !await LilithPlayerSaveCommand.RecoverBeforeServeAsync(CancellationToken.None).ConfigureAwait(false))
        {
            Console.Error.WriteLine("Lilith player data recovery must finish before smoke.");
            return 1;
        }
        await using var runtime = new LilithRuntimeService(runtimeOptions);
        LilithRuntimeSnapshot? startup = null;
        HermesNativeSurfaceProbeReceipt? probe = null;
        HermesStopReceipt? shutdown = null;
        try
        {
            startup = await runtime.StartAsync().ConfigureAwait(false);
            probe = await runtime.ProbeNativeSurfaceAsync().ConfigureAwait(false);
            shutdown = await runtime.StopAsync().ConfigureAwait(false);
            var report = new LilithRuntimeSmokeReport(startup, probe, shutdown);
            Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
            return shutdown.ProcessExited && shutdown.ReadySentinelRemoved ? 0 : 1;
        }
        catch (Exception exception)
        {
            try
            {
                shutdown ??= await runtime.StopAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            var failure = new
            {
                ok = false,
                stage = probe is not null
                    ? "shutdown"
                    : startup is not null
                        ? "probe"
                        : "startup",
                error = exception.Message,
                startup,
                probe,
                shutdown
            };
            Console.Error.WriteLine(JsonSerializer.Serialize(failure, jsonOptions));
            return 1;
        }
    }

    private static bool TryParseSmokeOptions(
        ReadOnlySpan<string> arguments,
        out bool compact,
        out string? smokeProfileRoot)
    {
        compact = false;
        smokeProfileRoot = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--compact", StringComparison.OrdinalIgnoreCase))
            {
                if (compact)
                    return false;
                compact = true;
                continue;
            }

            if (!string.Equals(
                    argument,
                    "--profile-root",
                    StringComparison.OrdinalIgnoreCase)
                || smokeProfileRoot is not null
                || index + 1 >= arguments.Length)
            {
                return false;
            }

            var candidate = arguments[++index];
            if (string.IsNullOrWhiteSpace(candidate)
                || !Path.IsPathFullyQualified(candidate))
            {
                return false;
            }
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var temporary = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath()));
            var relative = Path.GetRelativePath(temporary, full);
            if (relative is "." or ".."
                || Path.IsPathFullyQualified(relative)
                || relative.StartsWith(".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                || Directory.Exists(full)
                || File.Exists(full))
            {
                return false;
            }
            smokeProfileRoot = full;
        }
        return true;
    }

    private static async Task<int> RunServeAsync()
    {
        using var signals = new LilithRuntimeShutdownSignals(enableDesktopExit: true);
        try
        {
            LilithPreviousConfigRecovery.ThrowIfPending(LilithRuntimeOptions.DefaultHermesHomePath());
            using var playerDataLease = LilithRuntimeContracts.LilithPlayerSaveMaintenanceLease.TryAcquire(
                LilithRuntimeOptions.DefaultHermesHomePath());
            if (playerDataLease is null) return LilithRuntimeContracts.LilithRuntimeProcessExit.AlreadyRunning;
            var result = await new LilithRuntimeServeHost()
                .RunAsync(signals.Token)
                .ConfigureAwait(false);
            if (result == LilithRuntimeServeExit.AlreadyRunning)
            {
                Console.Error.WriteLine(
                    "Lilith Runtime is already running for the current user.");
            }

            return (int)result;
        }
        catch (OperationCanceledException) when (signals.Token.IsCancellationRequested)
        {
            return 0;
        }
        catch (LilithRuntimeException exception)
        {
            return ReportStartupFailure(exception, Console.Error);
        }
        catch (Exception exception)
        {
            return ReportStartupFailure(exception, Console.Error);
        }
    }

    internal static int ReportStartupFailure(Exception exception, TextWriter output)
    {
        // Only the closed diagnostic catalog crosses this boundary. No
        // provider leases, tokens, child stderr, paths or exception data.
        var exitCode = exception is LilithRuntimeException typed
            ? LilithRuntimeContracts.LilithRuntimeProcessExit.ForFailure(typed.FailureKind)
            : LilithRuntimeContracts.LilithRuntimeProcessExit.Failed;
        output.WriteLine(LilithRuntimeContracts.LilithRuntimeStartupDiagnostics.CodeForExit(exitCode));
        return exitCode;
    }
}
