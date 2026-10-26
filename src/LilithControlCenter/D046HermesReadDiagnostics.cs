using System.Diagnostics;
using System.Globalization;

namespace LilithControlCenter;

internal enum D046HermesReadOperation
{
    InitialSnapshot,
    CredentialStatus,
    DefaultModels,
    SessionModels,
    TelegramStatus,
    TelegramPairing,
    TelegramVoiceStatus
}

internal enum D046HermesReadPhase { Queue, Readiness, Response }
internal enum D046HermesReadOutcome { Completed, Failed, Cancelled }

internal sealed record D046HermesReadTiming(
    D046HermesReadOperation Operation,
    D046HermesReadOutcome Outcome,
    double QueueMilliseconds,
    double ReadinessMilliseconds,
    double ResponseMilliseconds)
{
    // All labels are closed enums; no request data or exception text enters logs.
    internal string ToSafeLogLine() => string.Create(CultureInfo.InvariantCulture,
        $"Hermes read operation={Operation} outcome={Outcome} queue_ms={QueueMilliseconds:F1} readiness_ms={ReadinessMilliseconds:F1} response_ms={ResponseMilliseconds:F1}");
}

internal sealed class D046HermesReadDiagnostics
{
    internal const int MaximumRetained = 64;
    internal const int MaximumPublished = 256;
    private readonly object _gate = new();
    private readonly Queue<D046HermesReadTiming> _recent = new();
    private readonly Func<double> _milliseconds;
    private int _published;

    internal D046HermesReadDiagnostics(Func<double>? milliseconds = null)
        => _milliseconds = milliseconds
            ?? (() => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

    internal event Action<D046HermesReadTiming>? Completed;

    internal IReadOnlyList<D046HermesReadTiming> Snapshot()
    {
        lock (_gate) return _recent.ToArray();
    }

    internal Measurement Begin(D046HermesReadOperation operation, CancellationToken cancellationToken)
        => new(this, operation, cancellationToken);

    private void Record(D046HermesReadTiming timing)
    {
        bool publish;
        lock (_gate)
        {
            _recent.Enqueue(timing);
            while (_recent.Count > MaximumRetained) _recent.Dequeue();
            publish = _published < MaximumPublished;
            if (publish) _published++;
        }
        // Diagnostics cannot throw into the transport or alter its authority.
        if (publish)
        {
            try { Completed?.Invoke(timing); }
            catch { }
        }
    }

    internal sealed class Measurement : IDisposable
    {
        private readonly D046HermesReadDiagnostics _owner;
        private readonly D046HermesReadOperation _operation;
        private readonly CancellationToken _cancellationToken;
        private readonly double[] _elapsed = new double[3];
        private D046HermesReadPhase _phase;
        private double _started;
        private bool _succeeded;
        private bool _disposed;

        internal Measurement(D046HermesReadDiagnostics owner,
            D046HermesReadOperation operation, CancellationToken cancellationToken)
        {
            _owner = owner;
            _operation = operation;
            _cancellationToken = cancellationToken;
            _started = owner._milliseconds();
        }

        internal void Advance(D046HermesReadPhase phase)
        {
            if (_disposed) return;
            var now = _owner._milliseconds();
            _elapsed[(int)_phase] += Math.Max(0, now - _started);
            _started = now;
            _phase = phase;
        }

        internal T Success<T>(T value)
        {
            _succeeded = true;
            return value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Advance(_phase);
            _disposed = true;
            _owner.Record(new(_operation,
                _succeeded ? D046HermesReadOutcome.Completed
                    : _cancellationToken.IsCancellationRequested
                        ? D046HermesReadOutcome.Cancelled : D046HermesReadOutcome.Failed,
                _elapsed[0], _elapsed[1], _elapsed[2]));
        }
    }
}
