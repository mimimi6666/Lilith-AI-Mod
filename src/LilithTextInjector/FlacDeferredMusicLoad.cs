using System;
using System.Collections;

namespace LilithTextInjector;

// Creating a nested native coroutine is not the same as starting a new track.
// Defer activation until the coroutine host actually advances this child, while
// an external-request admission gate fences children abandoned by stop/skip.
internal sealed class FlacDeferredMusicLoad : IEnumerator, IDisposable
{
    private readonly Func<IEnumerator> _start;
    private readonly Func<bool> _isAdmitted;
    private IEnumerator? _inner;
    private bool _started;
    private bool _hasCurrent;
    private bool _disposed;
    private bool _innerDisposed;

    internal FlacDeferredMusicLoad(Func<IEnumerator> start, Func<bool> isAdmitted)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _isAdmitted = isAdmitted ?? throw new ArgumentNullException(nameof(isAdmitted));
    }

    public object? Current
    {
        get
        {
            if (_disposed) return null;
            try
            {
                if (!_isAdmitted()) { Dispose(); return null; }
                if (!_hasCurrent || _inner is null) return null;
                var current = _inner.Current;
                if (_disposed || !_isAdmitted()) { Dispose(); return null; }
                return current;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    public bool MoveNext()
    {
        if (_disposed) return false;
        try
        {
            if (!_isAdmitted()) return Finish();
            if (!_started)
            {
                _started = true;
                _inner = _start() ?? throw new InvalidOperationException("Deferred music load did not create an iterator.");
                if (_disposed)
                {
                    DisposeInner();
                    return false;
                }
            }

            if (!_isAdmitted()) return Finish();
            _hasCurrent = false;
            var next = _inner!.MoveNext();
            if (_disposed || !next || !_isAdmitted()) return Finish();
            _hasCurrent = true;
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

    public void Reset() => throw new NotSupportedException("Deferred music loads cannot be reset.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hasCurrent = false;
        DisposeInner();
    }

    private void DisposeInner()
    {
        if (_inner is null || _innerDisposed) return;
        _innerDisposed = true;
        (_inner as IDisposable)?.Dispose();
    }
}
