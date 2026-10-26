using System;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal sealed class PlayerAuthoredIdentityShadowObservation
{
    internal PlayerAuthoredIdentityAnalysis Analysis { get; }
    internal PlayerAuthoredIdentityState State { get; }
    internal PlayerAuthoredIdentityLoadStatus InitialLoadStatus { get; }
    internal bool Persisted { get; }
    internal string Evidence { get; }
    internal string Axes { get; }
    internal string Ambivalence { get; }

    internal PlayerAuthoredIdentityShadowObservation(
        PlayerAuthoredIdentityAnalysis analysis,
        PlayerAuthoredIdentityState state,
        PlayerAuthoredIdentityLoadStatus initialLoadStatus,
        bool persisted)
    {
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        State = state ?? throw new ArgumentNullException(nameof(state));
        InitialLoadStatus = initialLoadStatus;
        Persisted = persisted;
        Evidence = analysis.Evidence.Count == 0
            ? "-"
            : string.Join(
                ",",
                analysis.Evidence.Select(item =>
                    $"{item.Kind}:{item.Confidence.ToString("F2", CultureInfo.InvariantCulture)}"));
        Axes = PlayerAuthoredIdentityCore.FormatAxisSummary(state);
        Ambivalence = state.OverallAmbivalence.ToString("F2", CultureInfo.InvariantCulture);
    }
}

internal sealed class PlayerAuthoredIdentityShadowRuntime
{
    private readonly object _gate = new();
    private readonly PlayerAuthoredIdentityStore _store;
    private PlayerAuthoredIdentityState _state;
    private readonly PlayerAuthoredIdentityLoadStatus _initialLoadStatus;

    internal PlayerAuthoredIdentityShadowRuntime(string statePath, DateTimeOffset now)
    {
        _store = new PlayerAuthoredIdentityStore(statePath);
        var loaded = _store.LoadOrDefault(now);
        _state = loaded.State;
        _initialLoadStatus = loaded.Status;
    }

    internal PlayerAuthoredIdentityState GetSnapshot()
    {
        lock (_gate)
            return _state;
    }

    internal PlayerAuthoredIdentityLoadStatus InitialLoadStatus =>
        _initialLoadStatus;

    internal PlayerAuthoredIdentityShadowObservation Observe(
        string userText,
        DateTimeOffset occurredAt)
    {
        lock (_gate)
        {
            var effectiveAt = occurredAt < _state.UpdatedAt ? _state.UpdatedAt : occurredAt;
            var analysis = PlayerAuthoredIdentityAnalyzer.Analyze(userText);
            var next = PlayerAuthoredIdentityCore.Apply(_state, analysis, effectiveAt);
            _store.Save(next);
            _state = next;
            return new PlayerAuthoredIdentityShadowObservation(
                analysis,
                next,
                _initialLoadStatus,
                persisted: true);
        }
    }
}
