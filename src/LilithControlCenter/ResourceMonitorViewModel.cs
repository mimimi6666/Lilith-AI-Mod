using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace LilithControlCenter;

/// <summary>Ephemeral presentation only. Unknown and stale metrics never paint a zero reading.</summary>
internal sealed class ResourceMonitorViewModel : INotifyPropertyChanged
{
    internal const int HistoryLimit = 30;
    private readonly Queue<(double? Download, double? Upload)> _history = new();
    private ResourceSnapshot? _snapshot;
    private bool _active;
    private static ControlCenterLocalization Texts => ControlCenterLocalization.Current;
    private static CultureInfo Culture => CultureInfo.GetCultureInfo(Texts.Language);
    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText => Texts.Get(!_active ? "resources.paused" : _snapshot is null
        ? "resources.warming" : "resources.local");
    public string CpuPercentText => Percent(_snapshot?.CpuUsage);
    public string GpuPercentText => Percent(_snapshot?.GpuUsage);
    public string MemoryPercentText => MemoryPercent is double value ? Number(value, "0") + "%" : "—";
    public string GpuTemperatureText => Temperature(_snapshot?.GpuTemperature);
    public string CpuStateText => State(_snapshot?.CpuUsage.State);
    public string GpuStateText => State(_snapshot?.GpuUsage.State);
    public string MemoryStateText => State(_snapshot?.Memory.State);
    public string MemoryBytesText => _snapshot?.Memory is { State: ResourceMetricState.Available, UsedBytes: ulong used, TotalBytes: ulong total }
        && total > 0 && used <= total ? Texts.Format("resources.memoryAmount", Number(used / 1073741824d, "0.0"), Number(total / 1073741824d, "0.0")) : "—";
    public double MemoryBarWidth => MemoryPercent ?? 0;
    public string CpuArc => Arc(Usage(_snapshot?.CpuUsage));
    public string GpuArc => Arc(Usage(_snapshot?.GpuUsage));
    public string CpuNeedle => Needle(Usage(_snapshot?.CpuUsage));
    public string GpuNeedle => Needle(Usage(_snapshot?.GpuUsage));
    public string DownloadText => Rate(NetworkRate(download: true));
    public string UploadText => Rate(NetworkRate(download: false));
    public string NetworkStateText => _snapshot?.Network.State == ResourceMetricState.Available
        ? Texts.Get("resources.history") : State(_snapshot?.Network.State);
    public string DownloadLine => History(download: true, area: false);
    public string DownloadArea => History(download: true, area: true);
    public string UploadLine => History(download: false, area: false);
    public string UploadArea => History(download: false, area: true);

    private double? MemoryPercent => _snapshot?.Memory is { State: ResourceMetricState.Available, UsedBytes: ulong used, TotalBytes: ulong total }
        && total > 0 && used <= total ? used * 100d / total : null;

    internal void Begin()
    {
        _active = true;
        _snapshot = null;
        _history.Clear();
        Notify();
    }

    internal void Pause()
    {
        _active = false;
        _snapshot = null;
        _history.Clear();
        Notify();
    }

    internal void Apply(ResourceSnapshot snapshot, DateTimeOffset? observedNow = null)
    {
        if (!_active) return;
        // The UI dispatcher can be delayed by unrelated work. Generation checks
        // alone do not make an old reading current once that work releases it.
        var age = (observedNow ?? DateTimeOffset.UtcNow) - snapshot.ObservedAtUtc;
        _snapshot = age > TimeSpan.FromSeconds(10) || age < TimeSpan.FromSeconds(-2)
            ? ResourceSnapshot.Missing(ResourceMetricState.Stale) : snapshot;
        _history.Enqueue((NetworkRate(download: true), NetworkRate(download: false)));
        while (_history.Count > HistoryLimit) _history.Dequeue();
        Notify();
    }

    internal void RefreshLanguage() => Notify();
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    private static string Number(double value, string format) => value.ToString(format, Culture);
    private static string State(ResourceMetricState? state) => Texts.Get(state switch
    {
        ResourceMetricState.Available => "resources.available",
        ResourceMetricState.WarmingUp or null => "resources.warming",
        ResourceMetricState.Unsupported => "resources.unsupported",
        ResourceMetricState.Stale => "resources.stale",
        _ => "resources.unavailable"
    });
    private static double? Usage(ResourceMetric? metric) => metric is { State: ResourceMetricState.Available, Value: double value }
        && double.IsFinite(value) && value >= 0 && value <= 100 ? value : null;
    private static string Percent(ResourceMetric? metric) => Usage(metric) is double value ? Number(value, "0") + "%" : "—";
    private static string Temperature(ResourceMetric? metric) => Texts.Format("resources.temperature",
        metric is { State: ResourceMetricState.Available, Value: double value } && double.IsFinite(value)
            && value >= -50 && value <= 200 ? Number(value, "0") + " °C" : "—");
    private double? NetworkRate(bool download)
    {
        if (_snapshot?.Network.State != ResourceMetricState.Available) return null;
        var value = download ? _snapshot.Network.DownloadBytesPerSecond : _snapshot.Network.UploadBytesPerSecond;
        return value is >= 0 && double.IsFinite(value.Value) ? value : null;
    }
    private static string Rate(double? value)
    {
        if (value is null) return "—";
        var units = new[] { "B/s", "KiB/s", "MiB/s", "GiB/s" };
        var amount = value.Value;
        var index = 0;
        while (amount >= 1024 && index < units.Length - 1) { amount /= 1024; index++; }
        return Number(amount, index == 0 ? "0" : "0.0") + " " + units[index];
    }
    private static string Arc(double? percent)
    {
        if (percent is null or <= 0) return string.Empty;
        var angle = Math.PI * (1 - percent.Value / 100);
        return FormattableString.Invariant($"M 7,82 A 75,75 0 0 1 {82 + 75 * Math.Cos(angle):0.###},{82 - 75 * Math.Sin(angle):0.###}");
    }
    private static string Needle(double? percent)
    {
        if (percent is null) return string.Empty;
        var angle = Math.PI * (1 - percent.Value / 100);
        var x = Math.Cos(angle);
        var y = -Math.Sin(angle);
        return FormattableString.Invariant($"M {82 + 57 * x:0.###},{82 + 57 * y:0.###} L {82 - 3 * y:0.###},{82 + 3 * x:0.###} L {82 + 3 * y:0.###},{82 - 3 * x:0.###} Z");
    }
    private string History(bool download, bool area)
    {
        var samples = _history.Select(item => download ? item.Download : item.Upload).ToArray();
        var maximum = Math.Max(1024, samples.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty().Max());
        var path = new StringBuilder();
        // A missing sample is a gap, never a zero or an interpolated successful read.
        for (var i = 0; i < samples.Length; i++)
        {
            if (samples[i] is null) continue;
            var start = i;
            while (i + 1 < samples.Length && samples[i + 1].HasValue) i++;
            if (i == start) continue; // One point has no observed time span.
            var xStart = (HistoryLimit - samples.Length + start) * 200d / (HistoryLimit - 1);
            if (area) path.Append(FormattableString.Invariant($"M {xStart:0.###},45 "));
            for (var index = start; index <= i; index++)
            {
                var x = (HistoryLimit - samples.Length + index) * 200d / (HistoryLimit - 1);
                var y = 45 - samples[index]!.Value / maximum * 42;
                path.Append(FormattableString.Invariant($"{(index == start && !area ? "M" : "L")} {x:0.###},{y:0.###} "));
            }
            if (area)
            {
                var xEnd = (HistoryLimit - samples.Length + i) * 200d / (HistoryLimit - 1);
                path.Append(FormattableString.Invariant($"L {xEnd:0.###},45 Z "));
            }
        }
        return path.ToString();
    }
}
