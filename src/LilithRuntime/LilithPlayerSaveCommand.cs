using System.Diagnostics;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed record LilithPlayerSaveCommandResult(bool Succeeded, string Code,
    long ConversationCount = 0, long MessageCount = 0, long MemoryCount = 0, long EpisodeCount = 0);

// Deliberately does not construct LilithRuntimeService or load Hermes configuration.
internal static class LilithPlayerSaveCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(15);

    internal static async Task<int> RunAsync(string[] args)
    {
        LilithPlayerSaveCommandResult result;
        try
        {
            if (!TryParseArguments(args, out var operation, out var archive))
                result = new(false, "player_save.invalid");
            else
            {
                using var lease = LilithPlayerSaveMaintenanceLease.TryAcquire(LilithRuntimeOptions.DefaultHermesHomePath());
                // Also respect already-running older Runtime installations that
                // predate the profile-file lease. Never stop a different install.
                using var runtimeLease = lease is null ? null : LilithRuntimeSingleInstanceLease.TryAcquireCurrentUser();
                result = lease is null || runtimeLease is null ? new(false, "player_save.busy")
                    : await ExecuteAsync(operation, archive, LilithRuntimeOptions.CreateDefault(), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { result = new(false, "player_save.io_failed"); }
        catch (Exception)
        { result = new(false, args.FirstOrDefault() == "data-import" ? "player_save.import_uncertain" : "player_save.failed"); }
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.Succeeded ? 0 : 1;
    }

    internal static bool TryParseArguments(string[] args, out string operation, out string? archive)
    {
        operation = ""; archive = null;
        if (args.Length != 3 || args[0] is not ("data-export" or "data-import")
            || args[1] != "--archive" || string.IsNullOrWhiteSpace(args[2]) || !Path.IsPathFullyQualified(args[2])) return false;
        operation = args[0] == "data-export" ? "export" : "import";
        archive = Path.GetFullPath(args[2]);
        return true;
    }

    internal static Task<bool> RecoverBeforeServeAsync(CancellationToken cancellationToken)
        => RecoverBeforeServeAsync(LilithRuntimeOptions.CreateDefault(), cancellationToken);

    internal static async Task<bool> RecoverBeforeServeAsync(
        LilithRuntimeOptions options, CancellationToken cancellationToken)
    {
        LilithPreviousConfigRecovery.ThrowIfPending(options.HermesHomePath);
        if (!LilithPlayerSaveMaintenanceLease.HasPendingTransaction(options.HermesHomePath)) return true;
        var result = await ExecuteAsync("recover", null, options, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && !LilithPlayerSaveMaintenanceLease.HasPendingTransaction(options.HermesHomePath);
    }

    internal static ProcessStartInfo CreateStartInfo(string operation, string? archive,
        LilithRuntimeOptions options, string runtimeDirectory)
    {
        if (operation is not ("export" or "import" or "recover")
            || (operation != "recover" && (archive is null || !Path.IsPathFullyQualified(archive))))
            throw new ArgumentException("Invalid player-save operation.");
        var info = new ProcessStartInfo
        {
            FileName = options.HermesExecutablePath,
            WorkingDirectory = Path.GetFullPath(runtimeDirectory),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add("-S");
        // -I ignores PYTHONDONTWRITEBYTECODE. Set the interpreter switch before
        // any stdlib import, or an offline save pollutes the pinned component.
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add(PlayerSaveScriptPath(runtimeDirectory));
        info.ArgumentList.Add(operation);
        info.ArgumentList.Add("--profile"); info.ArgumentList.Add(options.HermesHomePath);
        if (archive is not null) { info.ArgumentList.Add("--archive"); info.ArgumentList.Add(archive); }
        info.ArgumentList.Add("--hermes-source"); info.ArgumentList.Add(options.HermesSourcePath);
        // -I -S also prevents importing user packages/site hooks. Do not pass service
        // authority, profile-binding credentials or arbitrary Python startup knobs.
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("HERMES_", StringComparison.OrdinalIgnoreCase)
                     || key.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(key);
        return info;
    }

    private static string PlayerSaveScriptPath(string runtimeDirectory)
        => Path.Combine(Path.GetFullPath(runtimeDirectory), "Assets", "PlayerSave", "player_save.py");

    private static async Task<LilithPlayerSaveCommandResult> ExecuteAsync(string operation, string? archive,
        LilithRuntimeOptions options, CancellationToken cancellationToken)
    {
        var start = CreateStartInfo(operation, archive, options, AppContext.BaseDirectory);
        if (!File.Exists(start.FileName) || !File.Exists(PlayerSaveScriptPath(AppContext.BaseDirectory)))
            return new(false, "player_save.unavailable");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationTimeout);
        WindowsJobProcess? owned = null;
        try
        {
            owned = WindowsJobObject.StartProcess(start);
            var output = ReadBoundedAsync(owned.StandardOutput, deadline.Token);
            var error = DrainAsync(owned.StandardError, deadline.Token);
            await Task.WhenAll(output, error, owned.Process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return DecodeEngineResult(operation, await output.ConfigureAwait(false), owned.Process.ExitCode);
        }
        catch (OperationCanceledException)
        { return new(false, operation == "import" ? "player_save.import_uncertain" : "player_save.cancelled"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return new(false, operation == "import" ? "player_save.import_uncertain" : "player_save.io_failed"); }
        finally
        {
            if (owned is not null)
            {
                try
                {
                    if (owned.Job.ActiveProcessCount != 0)
                        await owned.Job.TerminateAndWaitAsync(1, TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                }
                finally { owned.Dispose(); }
            }
        }
    }

    internal static LilithPlayerSaveCommandResult DecodeEngineResult(string operation, string json, int exitCode)
    {
        var result = Decode(json, exitCode);
        return operation == "import" && !result.Succeeded && result.Code == "player_save.failed"
            ? result with { Code = "player_save.import_uncertain" } : result;
    }

    internal static LilithPlayerSaveCommandResult Decode(string json, int exitCode)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6) throw new JsonException();
            var success = root.GetProperty("succeeded").GetBoolean();
            var code = root.GetProperty("code").GetString()!;
            if (!IsKnownCode(code) || success != (exitCode == 0)
                || success != (code is "player_save.exported" or "player_save.imported" or "player_save.recovered")) throw new JsonException();
            var values = new[] { "conversationCount", "messageCount", "memoryCount", "episodeCount" }
                .Select(key => root.GetProperty(key).GetInt64()).ToArray();
            if (values.Any(value => value < 0)) throw new JsonException();
            return new(success, code, values[0], values[1], values[2], values[3]);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
        { return new(false, "player_save.failed"); }
    }

    private static bool IsKnownCode(string code) => code is "player_save.exported" or "player_save.imported" or "player_save.recovered"
        or "player_save.busy" or "player_save.invalid" or "player_save.conversion_failed" or "player_save.newer_format" or "player_save.io_failed"
        or "player_save.unavailable" or "player_save.failed" or "player_save.import_uncertain" or "player_save.cancelled";

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > 8192) throw new InvalidDataException("Invalid player-save result.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) != 0) { }
    }
}
