using System;
using System.Collections;

namespace LilithTextInjector;

// Driven by the native coroutine host, never by the decoder worker. This only
// selects a prepared local URL for a native load; it does not own music state.
internal sealed class FlacMusicLoadRoutine : IEnumerator, IDisposable
{
    private readonly FlacMusicPreparation _preparation;
    private readonly FlacMusicTicket _ticket;
    private readonly IEnumerator _native;
    private readonly Func<string, IDisposable> _enterNativeScope;
    private readonly Action<string> _reportFailure;
    private string? _playbackPath;
    private object? _current;
    private bool _disposed;

    internal FlacMusicLoadRoutine(FlacMusicPreparation preparation, FlacMusicTicket ticket,
        IEnumerator native, Func<string, IDisposable> enterNativeScope, Action<string> reportFailure)
    {
        _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        _ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _enterNativeScope = enterNativeScope ?? throw new ArgumentNullException(nameof(enterNativeScope));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public object? Current => _current;

    public bool MoveNext()
    {
        if (_disposed) return false;
        try
        {
            if (!_preparation.IsCurrent(_ticket)) return Finish();
            if (_playbackPath is null)
            {
                if (!_ticket.Completion.IsCompleted && !_ticket.IsCancelled)
                {
                    _current = null;
                    return true;
                }

                var ready = !_ticket.IsCancelled && _ticket.Completion.IsCompletedSuccessfully &&
                    _ticket.Completion.Result.Success;
                _playbackPath = ready ? _ticket.WavePath : _ticket.FailurePath;
                if (!ready)
                {
                    var reason = _ticket.IsCancelled ? "Timeout" :
                        _ticket.Completion.IsCompletedSuccessfully
                            ? _ticket.Completion.Result.FailureKind ?? "PreparationFailed"
                            : "PreparationFailed";
                    _reportFailure(reason);
                }
            }

            if (_disposed || !_preparation.IsCurrent(_ticket)) return Finish();
            var next = false;
            using (_enterNativeScope(_playbackPath))
            {
                // Scope construction and native callbacks may synchronously
                // replace the current request. Do not advance an obsolete one.
                if (!_disposed && _preparation.IsCurrent(_ticket)) next = _native.MoveNext();
            }

            if (_disposed || !_preparation.IsCurrent(_ticket) || !next) return Finish();
            _current = _native.Current;
            if (_disposed || !_preparation.IsCurrent(_ticket)) return Finish();
            return true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private bool Finish()
    {
        Dispose();
        return false;
    }

    public void Reset() => throw new NotSupportedException("Music load routines cannot be reset.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _current = null;
        try { (_native as IDisposable)?.Dispose(); }
        finally { _preparation.Release(_ticket); }
    }
}
