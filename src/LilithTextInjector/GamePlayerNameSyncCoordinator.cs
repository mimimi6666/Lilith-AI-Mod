using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// The caller observes Unity state on its main thread. This coordinator receives
// only immutable name data; it cannot read or write an archive or conversation.
internal sealed class GamePlayerNameSyncCoordinator
{
    private readonly object _gate = new();
    private readonly Func<Guid, long, string, CancellationToken,
        Task<LilithGameBridgePlayerNameResponse>> _send;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _timeout;
    private readonly Guid _gameEpoch = Guid.NewGuid();
    private string? _name;
    private long _sequence;
    private long _confirmedSequence;
    private Guid _generation;
    private long _generationFence;
    private int _attempts;
    private DateTimeOffset _retryAt;
    private Task<bool>? _inFlight;

    internal GamePlayerNameSyncCoordinator(
        Func<Guid, long, string, CancellationToken, Task<LilithGameBridgePlayerNameResponse>> send,
        Func<DateTimeOffset>? clock = null, TimeSpan? timeout = null)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    internal bool Observe(bool archiveReady, string? displayName)
    {
        if (!archiveReady || displayName is null) return false;
        try { LilithPlayerDisplayName.Validate(displayName); }
        catch (ArgumentException) { return false; }
        lock (_gate)
        {
            if (string.Equals(_name, displayName, StringComparison.Ordinal)) return true;
            if (_sequence == long.MaxValue) return false;
            _name = displayName;
            _sequence++;
            _attempts = 0;
            _retryAt = DateTimeOffset.MinValue;
            return true;
        }
    }

    internal void ObserveRuntimeGeneration(Guid generation)
    {
        if (generation == Guid.Empty) return;
        lock (_gate)
        {
            if (_generation == generation) return;
            _generation = generation;
            _generationFence++;
            _confirmedSequence = 0;
            _attempts = 0;
            _retryAt = DateTimeOffset.MinValue;
        }
    }

    internal Task<bool> SynchronizeAsync(bool explicitPlayerTurn = false)
    {
        TaskCompletionSource<bool> completion;
        string name;
        long sequence, fence;
        lock (_gate)
        {
            if (_name is null) return Task.FromResult(false);
            if (_inFlight is not null) return _inFlight;
            if (_confirmedSequence == _sequence) return Task.FromResult(true);
            // A new player turn may retry this optional fact once again. A
            // frame loop cannot generate an unbounded stream of failed writes.
            if (explicitPlayerTurn) { _attempts = 0; _retryAt = DateTimeOffset.MinValue; }
            if (_attempts >= 3 || _clock() < _retryAt) return Task.FromResult(false);
            name = _name;
            sequence = _sequence;
            fence = _generationFence;
            _attempts++;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = completion.Task;
        }
        _ = SendAsync(name, sequence, fence, completion);
        return completion.Task;
    }

    internal async Task<bool> SynchronizeLatestForPlayerTurnAsync(CancellationToken cancellationToken)
    {
        // A rename while an earlier observation is in flight gets one follow-up,
        // not a concurrent write. The caller also imposes a total wait bound.
        for (var pass = 0; pass < 2; pass++)
            if (await SynchronizeAsync(explicitPlayerTurn: pass == 0)
                    .WaitAsync(cancellationToken).ConfigureAwait(false)) return true;
        return false;
    }

    private async Task SendAsync(string name, long sequence, long fence,
        TaskCompletionSource<bool> completion)
    {
        LilithGameBridgePlayerNameResponse? response = null;
        try
        {
            using var timeout = new CancellationTokenSource(_timeout);
            response = await _send(_gameEpoch, sequence, name, timeout.Token).ConfigureAwait(false);
            if (response.GameEpoch != _gameEpoch || response.ObservationSequence != sequence)
                throw new InvalidDataException("Player-name receipt was not correlated.");
        }
        catch (Exception)
        {
            // Name metadata is optional. Never log its value or turn a failed
            // synchronization into a player-message failure or a save write.
            response = null;
        }
        bool confirmed;
        lock (_gate)
        {
            if (response is { Accepted: true } && fence == _generationFence)
            {
                _generation = response.GenerationId;
                _confirmedSequence = sequence;
                _attempts = 0;
                _retryAt = DateTimeOffset.MinValue;
            }
            else if (fence == _generationFence && sequence == _sequence)
                _retryAt = _clock().AddSeconds(_attempts == 1 ? 2 : 5);
            confirmed = _confirmedSequence == _sequence;
            _inFlight = null;
        }
        completion.TrySetResult(confirmed);
    }
}
