using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal sealed record ChessEngineRequest(string Fen, string[] Moves, string[] LegalMoves, int Revision, int Strength);
internal sealed record ChessEngineReply(string Move, int Revision);

// A separate, owned Stockfish process. Never runs on Unity's thread, never
// receives arbitrary commands/FEN from UI/model/network, and cannot choose an
// illegal move. The session validates revision, actor and legality again.
internal sealed class ChessEngine : IDisposable
{
    private readonly string executable;
    private readonly string expectedHash;
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private bool disposed;
    internal int? OwnedProcessId => process?.Id;
    internal long PeakMemoryBytes { get; private set; }
    internal double TotalCpuMilliseconds { get; private set; }
    internal ChessEngine(string executablePath, string sha256) { executable = executablePath; expectedHash = sha256; }

    internal async Task<ChessEngineReply> ChooseAsync(ChessEngineRequest request, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(12)); // includes startup on slow CPUs
        var token = linked.Token;
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (disposed) throw new ObjectDisposedException(nameof(ChessEngine));
            ValidateRequest(request);
            await EnsureStarted(token).ConfigureAwait(false);
            var writer = process!.StandardInput;
            await writer.WriteLineAsync("setoption name UCI_LimitStrength value true").ConfigureAwait(false);
            await writer.WriteLineAsync("setoption name UCI_Elo value " + Math.Clamp(request.Strength, 1320, 2800).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            await writer.WriteLineAsync("position fen " + request.Fen + (request.Moves.Length > 0 ? " moves " + string.Join(" ", request.Moves) : "")).ConfigureAwait(false);
            var nodes = Math.Clamp(12000 + (request.Strength - 400) * 60, 12000, 160000);
            await writer.WriteLineAsync("go nodes " + nodes.ToString(CultureInfo.InvariantCulture) + " movetime 450").ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            using var search = CancellationTokenSource.CreateLinkedTokenSource(token);
            search.CancelAfter(TimeSpan.FromSeconds(4));
            var line = await ReadUntil("bestmove ", search.Token).ConfigureAwait(false);
            var best = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            token.ThrowIfCancellationRequested();
            process!.Refresh();
            PeakMemoryBytes = Math.Max(PeakMemoryBytes, process.PeakWorkingSet64);
            TotalCpuMilliseconds = process.TotalProcessorTime.TotalMilliseconds;
            if (!request.LegalMoves.Contains(best, StringComparer.Ordinal)) throw new InvalidDataException("Engine proposed an illegal move.");
            // Below Stockfish's calibrated floor, explicitly use a casual
            // mistake policy. This is a game difficulty, NOT a claimed Elo.
            var mistakeChance = Math.Clamp((1320 - request.Strength) / 1100.0, 0, 0.82);
            var chosen = Random.Shared.NextDouble() < mistakeChance
                ? request.LegalMoves[Random.Shared.Next(request.LegalMoves.Length)] : best;
            return new ChessEngineReply(chosen, request.Revision);
        }
        catch { StopOwnedProcess(); throw; }
        finally { serial.Release(); }
    }

    private static void ValidateRequest(ChessEngineRequest request)
    {
        if (request.Fen.Length > 128 || request.Fen.Any(c => !(c <= 127 && (char.IsLetterOrDigit(c) || c is '/' or ' ' or '-')))
            || request.Moves.Length > 2048 || request.LegalMoves.Length is < 1 or > 256
            || request.Moves.Concat(request.LegalMoves).Any(m => !IsUciMove(m))) throw new InvalidDataException("Invalid chess request.");
    }

    internal static bool IsUciMove(string? move) => move != null && move.Length is 4 or 5
        && move[0] is >= 'a' and <= 'h' && move[1] is >= '1' and <= '8'
        && move[2] is >= 'a' and <= 'h' && move[3] is >= '1' and <= '8'
        && (move.Length == 4 || move[4] is 'q' or 'r' or 'b' or 'n');

    private async Task EnsureStarted(CancellationToken token)
    {
        if (process != null && !process.HasExited) return;
        if (!File.Exists(executable)) throw new FileNotFoundException("Chess engine missing.");
        foreach (var path in new[] { executable, Path.GetDirectoryName(executable)! })
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Redirected chess engine.");
        using (var file = File.OpenRead(executable))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chess engine identity mismatch.");
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        process = new Process { StartInfo = start };
        process.ErrorDataReceived += (_, _) => { }; // drain; never forward engine output into UI
        if (!process.Start()) throw new IOException("Chess engine did not start.");
        process.BeginErrorReadLine();
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { }
        var input = process.StandardInput;
        await input.WriteLineAsync("uci").ConfigureAwait(false);
        await input.FlushAsync().ConfigureAwait(false);
        await ReadUntil("uciok", token).ConfigureAwait(false);
        await input.WriteLineAsync("setoption name Threads value 1").ConfigureAwait(false);
        await input.WriteLineAsync("setoption name Hash value 16").ConfigureAwait(false);
        await input.WriteLineAsync("setoption name Ponder value false").ConfigureAwait(false);
        await input.WriteLineAsync("ucinewgame").ConfigureAwait(false);
        await input.WriteLineAsync("isready").ConfigureAwait(false);
        await input.FlushAsync().ConfigureAwait(false);
        await ReadUntil("readyok", token).ConfigureAwait(false);
    }

    private async Task<string> ReadUntil(string prefix, CancellationToken token)
    {
        for (var count = 0; count < 4096; count++)
        {
            var line = await process!.StandardOutput.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
            if (line == null || line.Length > 16384) throw new IOException("Chess engine output unavailable.");
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
        }
        throw new IOException("Chess engine output budget exhausted.");
    }

    private void StopOwnedProcess()
    {
        var owned = Interlocked.Exchange(ref process, null);
        if (owned == null) return;
        try { if (!owned.HasExited) owned.Kill(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { owned.Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        // Worker owns process teardown to avoid concurrent stream disposal.
        // Idle process is stopped immediately; busy worker sees cancellation.
        if (serial.Wait(0)) { try { StopOwnedProcess(); } finally { serial.Release(); } }
    }
}
