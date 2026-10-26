using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LilithControlCenter;

internal sealed record ControlCenterPlayerSaveResult(bool Succeeded, string Code,
    long ConversationCount = 0, long MessageCount = 0, long MemoryCount = 0, long EpisodeCount = 0);

internal static class ControlCenterPlayerSaveState
{
    private static int _busy;
    private static int _restartRequired;
    internal static bool IsBusy => Volatile.Read(ref _busy) != 0;
    internal static bool RestartRequired => Volatile.Read(ref _restartRequired) != 0;
    internal static string? BootstrapBlockCode => RestartRequired ? "runtime.player_save_restart_required"
        : IsBusy ? "runtime.player_save_busy" : null;
    internal static bool TryBegin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    internal static void RequireRestart() => Volatile.Write(ref _restartRequired, 1);
    internal static void End() => Volatile.Write(ref _busy, 0);
}

internal sealed partial class ControlCenterPlayerSaveClient
{
    private readonly Func<Task<bool>> _stop;
    private readonly Func<string, string, CancellationToken, Task<ControlCenterPlayerSaveResult>> _run;
    internal static bool RestartRequired => ControlCenterPlayerSaveState.RestartRequired;

    internal ControlCenterPlayerSaveClient(Func<Task<bool>> stop,
        Func<string, string, CancellationToken, Task<ControlCenterPlayerSaveResult>> run)
    { _stop = stop; _run = run; }

    internal Task<ControlCenterPlayerSaveResult> ExportAsync(string path, CancellationToken token)
        => TransferAsync("data-export", path, token);
    internal Task<ControlCenterPlayerSaveResult> ImportAsync(string path, CancellationToken token)
        => TransferAsync("data-import", path, token);

    private async Task<ControlCenterPlayerSaveResult> TransferAsync(string command, string path, CancellationToken token)
    {
        if (token.IsCancellationRequested) return new(false, "player_save.cancelled");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return new(false, "player_save.invalid");
        if (!ControlCenterPlayerSaveState.TryBegin()) return new(false, "player_save.busy");
        try
        {
            if (!await _stop().ConfigureAwait(false)) return new(false, "player_save.busy");
            // A successful stop invalidates every existing chat/management projection.
            // Leave them retired until ordinary application restart, even after export.
            ControlCenterPlayerSaveState.RequireRestart();
            token.ThrowIfCancellationRequested();
            return await _run(command, Path.GetFullPath(path), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(false, "player_save.cancelled"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return new(false, "player_save.io_failed"); }
        catch (Exception) { return new(false, "player_save.failed"); }
        finally { ControlCenterPlayerSaveState.End(); }
    }

    internal static ProcessStartInfo CreateStartInfo(string runtimeDirectory, string command, string archive)
    {
        if (command is not ("data-export" or "data-import") || !Path.IsPathFullyQualified(archive)
            || !Path.IsPathFullyQualified(runtimeDirectory)) throw new ArgumentException("Invalid save command.");
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(runtimeDirectory, "LilithRuntime.exe"), WorkingDirectory = runtimeDirectory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(command); info.ArgumentList.Add("--archive"); info.ArgumentList.Add(archive);
        return info;
    }

    internal static async Task<ControlCenterPlayerSaveResult> RunProcessAsync(string runtimeDirectory,
        string command, string archive, CancellationToken token)
    {
        var info = CreateStartInfo(runtimeDirectory, command, archive);
        if (!File.Exists(info.FileName)) return new(false, "player_save.unavailable");
        using var process = Process.Start(info);
        if (process is null) return new(false, "player_save.unavailable");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(16));
        try
        {
            var output = ReadOutputAsync(process.StandardOutput, retain: true, deadline.Token);
            var error = ReadOutputAsync(process.StandardError, retain: false, deadline.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return Decode(await output.ConfigureAwait(false), process.ExitCode);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Only our exact helper is owned here. Its kill-on-close Python Job
            // contains all engine writes; no name-wide kill or unrelated tree scan.
            try
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception) { }
            return new(false, command == "data-import" ? "player_save.import_uncertain"
                : exception is OperationCanceledException ? "player_save.cancelled" : "player_save.io_failed");
        }
    }

    internal static ControlCenterPlayerSaveResult Decode(string json, int exitCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6) throw new JsonException();
            var success = root.GetProperty("succeeded").GetBoolean();
            var code = root.GetProperty("code").GetString()!;
            if (success != (exitCode == 0) || code is not ("player_save.exported" or "player_save.imported"
                or "player_save.busy" or "player_save.invalid" or "player_save.conversion_failed" or "player_save.newer_format" or "player_save.io_failed"
                or "player_save.unavailable" or "player_save.failed" or "player_save.import_uncertain" or "player_save.cancelled"))
                throw new JsonException();
            if (success != (code is "player_save.exported" or "player_save.imported")) throw new JsonException();
            var counts = new[] { "conversationCount", "messageCount", "memoryCount", "episodeCount" }
                .Select(key => root.GetProperty(key).GetInt64()).ToArray();
            if (counts.Any(value => value < 0)) throw new JsonException();
            return new(success, code, counts[0], counts[1], counts[2], counts[3]);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return new(false, "player_save.failed"); }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, bool retain, CancellationToken token)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[1024]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (!retain) continue;
            if (result.Length + count > 8192) throw new InvalidDataException("Invalid save result.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
