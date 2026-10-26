using System;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

// Process-local preparation only. No player text, playback or persisted state.
internal sealed class LocalVoiceWarmup
{
    private readonly object _gate = new();
    private Entry? _current;

    private sealed class Entry
    {
        internal readonly string Epoch;
        internal readonly string Endpoint;
        internal readonly CancellationTokenSource Stop = new();
        internal Task<bool> Work = Task.FromResult(false);
        internal Entry(string epoch, string endpoint) { Epoch = epoch; Endpoint = endpoint; }
    }

    internal static bool IsBundledEndpoint(string endpoint, bool japanese)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == "http" && uri.Host == "127.0.0.1"
            && uri.Port == (japanese ? 9881 : 9880)
            && uri.AbsolutePath == "/tts" && uri.Query.Length == 0
            && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;
    }

    internal Task<bool> Start(string epoch, string endpoint,
        Func<CancellationToken, Task> prepare, TimeSpan deadline)
    {
        if (string.IsNullOrWhiteSpace(epoch) || deadline <= TimeSpan.Zero)
            throw new ArgumentException("A bounded host epoch is required.");
        lock (_gate)
        {
            if (_current is { } existing && existing.Epoch == epoch
                && existing.Endpoint == endpoint)
                return existing.Work;
            CancelCurrentLocked();
            var entry = new Entry(epoch, endpoint);
            _current = entry;
            entry.Stop.CancelAfter(deadline);
            entry.Work = Task.Run(async () =>
            {
                try
                {
                    entry.Stop.Token.ThrowIfCancellationRequested();
                    await prepare(entry.Stop.Token).WaitAsync(entry.Stop.Token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (!ReferenceEquals(_current, entry) || entry.Stop.IsCancellationRequested)
                            return false;
                        entry.Stop.CancelAfter(Timeout.InfiniteTimeSpan);
                        return true;
                    }
                }
                catch (Exception) { return false; }
                // CTS remains valid while waiters or Reset inspect this entry.
            });
            return entry.Work;
        }
    }

    internal async Task WaitAsync(string endpoint, CancellationToken cancellationToken)
    {
        Entry? entry;
        lock (_gate) entry = _current;
        if (entry == null || entry.Endpoint != endpoint) return;
        // Cancelling F8's waiter must not cancel startup preparation for others.
        await entry.Work.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            if (!ReferenceEquals(_current, entry))
                throw new OperationCanceledException("The voice host epoch changed.");
        // A failed warmup is not an infinite retry / permanent speech failure.
        // The caller may make its normal bounded synthesis attempt.
    }

    internal void Reset()
    {
        lock (_gate) CancelCurrentLocked();
    }

    private void CancelCurrentLocked()
    {
        var entry = _current;
        _current = null;
        if (entry == null) return;
        entry.Stop.Cancel();
        _ = entry.Work.ContinueWith(_ => entry.Stop.Dispose(),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
