using System.ComponentModel;
using System.Globalization;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal sealed class LatestTurnUsageViewModel : INotifyPropertyChanged
{
    private HermesLatestUsageSnapshot? _snapshot;
    private HermesLatestUsageSnapshot? _lastAccepted;
    private string? _notice = "usage.latest.waiting";
    private static ControlCenterLocalization T => ControlCenterLocalization.Current;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string StateText => T[_notice ?? "usage.latest." + _snapshot!.State];
    public string InputText => Format(_snapshot?.InputTokens);
    public string OutputText => Format(_snapshot?.OutputTokens);
    public string CachedText => Format(_snapshot?.CacheReadTokens);
    public long? UncachedTokens => _snapshot?.InputTokens is { } input && _snapshot.CacheReadTokens is { } read ? input - read : null;
    public string UncachedText => Format(UncachedTokens);
    public string HitRateText => _snapshot?.InputTokens is > 0 && _snapshot.CacheReadTokens is { } read
        ? ((double)read / _snapshot.InputTokens.Value).ToString("P1", CultureInfo.GetCultureInfo(T.Language)) : T["usage.latest.unknown"];
    public string RequestsText => _snapshot?.TurnId is null ? string.Empty : T.Format("usage.latest.requests", _snapshot.RequestCount);
    public bool HasChart => _notice is null && _snapshot?.InputTokens is { } input && _snapshot.OutputTokens is { } output
        && _snapshot.CacheReadTokens is not null && (input > 0 || output > 0);
    public string ChartNote => _snapshot?.State == "none" ? T["usage.latest.none"] : T["usage.latest.chartUnknown"];
    public string CachedArc => Arc(0, _snapshot?.CacheReadTokens);
    public string UncachedArc => Arc(_snapshot?.CacheReadTokens ?? 0, UncachedTokens);
    public string OutputArc => Arc(_snapshot?.InputTokens ?? 0, _snapshot?.OutputTokens);
    internal HermesLatestUsageSnapshot? Snapshot => _snapshot;
    internal void Apply(HermesLatestUsageSnapshot? snapshot)
    {
        if (snapshot is null) { _snapshot = null; _notice = "usage.latest.unsupported"; RefreshLanguage(); return; }
        if (_lastAccepted is not null && snapshot.Revision < _lastAccepted.Revision) return;
        if (_lastAccepted is not null && snapshot.Revision == _lastAccepted.Revision && snapshot != _lastAccepted)
        { Unavailable(); return; }
        _lastAccepted = _snapshot = snapshot; _notice = null; RefreshLanguage();
    }
    internal void Reset() { _lastAccepted = _snapshot = null; _notice = "usage.latest.waiting"; RefreshLanguage(); }
    internal void Unavailable() { _snapshot = null; _notice = "usage.latest.unavailable"; RefreshLanguage(); }
    public void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private static string Format(long? count) => count is { } value ? value.ToString("N0", CultureInfo.GetCultureInfo(T.Language)) : T["usage.latest.unknown"];
    private string Arc(long start, long? count)
    {
        if (!HasChart || count is null or <= 0) return string.Empty;
        var total = (double)_snapshot!.InputTokens!.Value + _snapshot.OutputTokens!.Value;
        const double radius = 53, center = 60;
        var first = start / total * Math.PI * 2 - Math.PI / 2;
        var sweep = count.Value / total * Math.PI * 2;
        var x1 = center + radius * Math.Cos(first); var y1 = center + radius * Math.Sin(first);
        if (count.Value == total)
            return FormattableString.Invariant($"M {center},{center} L {x1},{y1} A {radius},{radius} 0 1 1 {center - radius * Math.Cos(first)},{center - radius * Math.Sin(first)} A {radius},{radius} 0 1 1 {x1},{y1} Z");
        var x2 = center + radius * Math.Cos(first + sweep); var y2 = center + radius * Math.Sin(first + sweep);
        return FormattableString.Invariant($"M {center},{center} L {x1},{y1} A {radius},{radius} 0 {(sweep > Math.PI ? 1 : 0)} 1 {x2},{y2} Z");
    }
}
