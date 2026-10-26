using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

// No Unity access on workers. The caller owns the native playback generation;
// stopping/skipping invalidates a ticket even when a Windows codec is still busy.
internal sealed class FlacMusicPreparation
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _decoderSlot = new(1, 1);
    private readonly Action<string, string, CancellationToken> _decode;
    private readonly string _directory;
    private readonly string _cacheParent;
    private readonly TimeSpan _timeout;
    private readonly HashSet<string> _retiredFiles = new(StringComparer.OrdinalIgnoreCase);
    private FlacMusicTicket? _current;
    private bool _recoveryAttempted;

    internal FlacMusicPreparation(string cacheParent,
        Action<string, string, CancellationToken> decode, TimeSpan? timeout = null)
    {
        _cacheParent = Path.GetFullPath(cacheParent);
        _directory = Path.Combine(_cacheParent, FlacMusicCacheRecovery.NewDirectoryName(Environment.ProcessId));
        _decode = decode;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    internal static bool IsFlac(string? path) =>
        path is not null && string.Equals(Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase);

    internal static string[] WithFlac(IEnumerable<string> extensions)
    {
        var result = extensions.ToList();
        if (!result.Any(e => string.Equals(e, ".flac", StringComparison.OrdinalIgnoreCase)))
            result.Add(".flac");
        return result.ToArray();
    }

    internal FlacMusicTicket Begin(string source)
    {
        lock (_gate)
        {
            RetireCurrent();
            var ticket = new FlacMusicTicket(source,
                Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".wav"), _timeout);
            _current = ticket;
            ticket.Completion = Task.Run(() => PrepareAsync(ticket));
            _ = ticket.Completion.ContinueWith(completed =>
            {
                lock (_gate)
                {
                    // Close the gap between the worker's finally and the Task
                    // becoming completed: retirement can occur in that gap.
                    if (ticket.IsCancelled || !ReferenceEquals(ticket, _current) || !completed.Result.Success)
                        TryDeleteOwned(ticket.WavePath);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return ticket;
        }
    }

    internal bool IsCurrent(FlacMusicTicket ticket)
    {
        lock (_gate) return ReferenceEquals(ticket, _current);
    }

    internal bool HasCurrent
    {
        get { lock (_gate) return _current is not null; }
    }

    internal void Cancel()
    {
        lock (_gate) RetireCurrent();
    }

    internal void Release(FlacMusicTicket ticket)
    {
        lock (_gate)
        {
            if (ReferenceEquals(ticket, _current)) RetireCurrent();
            else Retire(ticket);
        }
    }

    private void RetireCurrent()
    {
        if (_current is null) return;
        Retire(_current);
        _current = null;
    }

    private void Retire(FlacMusicTicket ticket)
    {
        ticket.Cancel();
        // A running writer cleans itself after finishing; never delete its file
        // from under Media Foundation. Finished files are exclusively ours.
        if (ticket.Completion.IsCompleted) TryDeleteOwned(ticket.WavePath);
    }

    private async Task<FlacPreparationResult> PrepareAsync(FlacMusicTicket ticket)
    {
        var entered = false;
        try
        {
            await _decoderSlot.WaitAsync(ticket.Token).ConfigureAwait(false);
            entered = true;
            ticket.Token.ThrowIfCancellationRequested();
            if (!_recoveryAttempted)
            {
                _recoveryAttempted = true;
                FlacMusicCacheRecovery.CollectAbandoned(_cacheParent, DateTime.UtcNow, ProcessIsAlive);
            }
            lock (_gate)
            {
                foreach (var old in _retiredFiles.ToArray()) TryDeleteOwned(old);
                // Do not accumulate another song if an earlier file is locked.
                if (_retiredFiles.Count != 0) throw new IOException("FLAC temporary file is still in use.");
            }
            EnsureOwnedDirectory();
            _decode(ticket.SourcePath, ticket.WavePath, ticket.Token);
            ticket.Token.ThrowIfCancellationRequested();
            return new FlacPreparationResult(true, null);
        }
        catch (Exception error)
        {
            return new FlacPreparationResult(false, error.GetType().Name);
        }
        finally
        {
            lock (_gate)
            {
                if (ticket.IsCancelled || !ReferenceEquals(ticket, _current))
                    TryDeleteOwned(ticket.WavePath);
            }
            if (entered) _decoderSlot.Release();
        }
    }

    private void EnsureOwnedDirectory()
    {
        // Cache is app-owned, not the user's music folder. Refuse redirects so
        // later cleanup can never traverse a junction into unrelated content.
        VerifyCacheAncestors();
        Directory.CreateDirectory(_directory);
    }

    private static bool ProcessIsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        // Inspection errors are deliberately not evidence of process death.
    }

    private void VerifyCacheAncestors()
    {
        for (var item = new DirectoryInfo(_directory); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked FLAC cache directories are not supported.");
    }

    private void TryDeleteOwned(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), _directory,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unowned FLAC cache path.");
        try
        {
            VerifyCacheAncestors();
            if (File.Exists(path)) File.Delete(path);
            _retiredFiles.Remove(path);
            if ((_current is null || _current.Completion.IsCompleted) &&
                Directory.Exists(_directory) && !Directory.EnumerateFileSystemEntries(_directory).Any())
                Directory.Delete(_directory, false);
        }
        catch (IOException) { _retiredFiles.Add(path); }
        catch (UnauthorizedAccessException) { _retiredFiles.Add(path); }
    }
}

internal sealed class FlacMusicTicket
{
    private readonly CancellationTokenSource _cancel;
    internal string SourcePath { get; }
    internal string WavePath { get; }
    internal string FailurePath => WavePath + ".unavailable";
    internal CancellationToken Token => _cancel.Token;
    internal bool IsCancelled => _cancel.IsCancellationRequested;
    internal Task<FlacPreparationResult> Completion { get; set; } =
        Task.FromResult(new FlacPreparationResult(false, "NotStarted"));

    internal FlacMusicTicket(string source, string wave, TimeSpan timeout)
    {
        SourcePath = source;
        WavePath = wave;
        _cancel = new CancellationTokenSource(timeout);
    }

    internal void Cancel() => _cancel.Cancel();
}

internal sealed record FlacPreparationResult(bool Success, string? FailureKind);
