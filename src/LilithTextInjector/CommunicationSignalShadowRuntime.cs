using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal sealed class CommunicationSignalShadowObservation
{
    internal CommunicationSignalAnalysis Analysis { get; }
    internal CommunicationPatternState State { get; }
    internal CommunicationPatternLoadStatus InitialLoadStatus { get; }
    internal bool Persisted { get; }

    internal CommunicationSignalShadowObservation(
        CommunicationSignalAnalysis analysis,
        CommunicationPatternState state,
        CommunicationPatternLoadStatus initialLoadStatus,
        bool persisted)
    {
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        State = state ?? throw new ArgumentNullException(nameof(state));
        InitialLoadStatus = initialLoadStatus;
        Persisted = persisted;
    }

    internal string Tones => Format(Analysis.Tones);
    internal string Attitudes => Format(Analysis.Attitudes);
    internal string Styles => Format(Analysis.Styles);
    internal string Strategies => Format(Analysis.Strategies);
    internal string Patterns => string.Join(
        ",",
        CommunicationPatternCore.GetActivePatterns(State)
            .Select(item => $"{item.Kind}:{item.Level}:{item.Score.ToString("F2", CultureInfo.InvariantCulture)}"));

    private static string Format<T>(IReadOnlyList<ScoredCommunicationSignal<T>> signals)
        where T : struct, Enum
        => signals.Count == 0
            ? "-"
            : string.Join(
                ",",
                signals.Select(signal =>
                    $"{signal.Kind}:{signal.Confidence.ToString("F2", CultureInfo.InvariantCulture)}"));
}

internal sealed class CommunicationSignalShadowRuntime
{
    private readonly object _gate = new();
    private readonly CommunicationPatternStore _store;
    private CommunicationPatternState _state;
    private readonly CommunicationPatternLoadStatus _initialLoadStatus;

    internal CommunicationSignalShadowRuntime(string statePath, DateTimeOffset now)
    {
        _store = new CommunicationPatternStore(statePath);
        var loaded = _store.LoadOrDefault(now);
        _state = loaded.State;
        _initialLoadStatus = loaded.Status;
    }

    internal CommunicationPatternState GetSnapshot()
    {
        lock (_gate)
            return _state;
    }

    internal CommunicationPatternLoadStatus InitialLoadStatus =>
        _initialLoadStatus;

    internal CommunicationSignalShadowObservation Observe(
        string userText,
        DateTimeOffset occurredAt)
    {
        lock (_gate)
        {
            var effectiveAt = occurredAt < _state.UpdatedAt ? _state.UpdatedAt : occurredAt;
            var analysis = CommunicationSignalAnalyzer.Analyze(userText, _state);
            var next = CommunicationPatternCore.Apply(_state, analysis, effectiveAt);
            _store.Save(next);
            _state = next;
            return new CommunicationSignalShadowObservation(
                analysis,
                next,
                _initialLoadStatus,
                persisted: true);
        }
    }
}
