using System.Globalization;

namespace LilithControlCenter;

// Process-local presentation only. No hardware identifiers leave the sampler.
internal enum ResourceMetricState { Available, WarmingUp, Unavailable, Unsupported, Stale }

internal sealed record ResourceMetric(double? Value, ResourceMetricState State)
{
    internal static ResourceMetric Missing(ResourceMetricState state) => new(null, state);
    internal static ResourceMetric Available(double value) => double.IsFinite(value)
        ? new(value, ResourceMetricState.Available) : Missing(ResourceMetricState.Unavailable);
}

internal sealed record ResourceMemory(ulong? UsedBytes, ulong? TotalBytes, ResourceMetricState State)
{
    public double? Percent => State == ResourceMetricState.Available && UsedBytes.HasValue
        && TotalBytes is > 0 && UsedBytes <= TotalBytes ? UsedBytes.Value * 100d / TotalBytes.Value : null;
    internal static ResourceMemory Missing(ResourceMetricState state) => new(null, null, state);
}

internal sealed record ResourceNetwork(double? DownloadBytesPerSecond, double? UploadBytesPerSecond,
    ResourceMetricState State, int AdapterCount)
{
    internal static ResourceNetwork Missing(ResourceMetricState state) => new(null, null, state, 0);
}

internal sealed record ResourceSnapshot(DateTimeOffset ObservedAtUtc, ResourceMetric CpuUsage,
    ResourceMetric GpuUsage, ResourceMetric CpuTemperature, ResourceMetric GpuTemperature,
    ResourceMemory Memory, ResourceNetwork Network, int GpuAdapterCount)
{
    internal static ResourceSnapshot Missing(ResourceMetricState state) => new(DateTimeOffset.UtcNow,
        ResourceMetric.Missing(state), ResourceMetric.Missing(state),
        ResourceMetric.Missing(ResourceMetricState.Unsupported), ResourceMetric.Missing(state),
        ResourceMemory.Missing(state), ResourceNetwork.Missing(state), 0);
}

internal interface IResourceSampler : IDisposable
{
    Task<ResourceSnapshot> ReadAsync(CancellationToken cancellationToken);
    void Reset();
}

internal readonly record struct ResourceCpuTimes(ulong Idle, ulong Kernel, ulong User);
internal readonly record struct ResourceNetworkCounter(string Id, long Received, long Sent);
internal readonly record struct ResourceGpuAdapter(uint LuidHigh, uint LuidLow, uint PhysicalIndex);
internal readonly record struct ResourceGpuEngine(ResourceGpuAdapter Adapter, uint Engine, double Percent);
internal sealed record ResourceGpuAggregate(ResourceMetric Usage, ResourceGpuAdapter? Selected, int AdapterCount);

internal static class ResourceSampleMath
{
    internal static ResourceMetric CpuPercent(ResourceCpuTimes? previous, ResourceCpuTimes current)
    {
        if (previous is not { } before) return ResourceMetric.Missing(ResourceMetricState.WarmingUp);
        if (current.Idle < before.Idle || current.Kernel < before.Kernel || current.User < before.User)
            return ResourceMetric.Missing(ResourceMetricState.WarmingUp);
        // Kernel includes idle time. Convert before adding so unsigned addition cannot overflow.
        var total = (double)(current.Kernel - before.Kernel) + (current.User - before.User);
        var idle = (double)(current.Idle - before.Idle);
        return total > 0 && idle <= total
            ? ResourceMetric.Available((total - idle) * 100d / total)
            : ResourceMetric.Missing(ResourceMetricState.WarmingUp);
    }

    internal static ResourceMemory Memory(ulong total, ulong available) => total > 0 && available <= total
        ? new(total - available, total, ResourceMetricState.Available)
        : ResourceMemory.Missing(ResourceMetricState.Unavailable);

    internal static ResourceNetwork Network(IReadOnlyList<ResourceNetworkCounter>? previous,
        IReadOnlyList<ResourceNetworkCounter> current, double seconds)
    {
        if (previous is null || !double.IsFinite(seconds) || seconds < .1 || seconds > 10)
            return new(null, null, ResourceMetricState.WarmingUp, current.Count);
        if (current.Count == 0) return ResourceNetwork.Missing(ResourceMetricState.Unavailable);
        // A changed adapter set/reset requires a new baseline, never a negative or false-zero speed.
        if (previous.Count != current.Count || previous.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != previous.Count)
            return new(null, null, ResourceMetricState.WarmingUp, current.Count);
        var before = previous.ToDictionary(x => x.Id, StringComparer.Ordinal);
        double received = 0, sent = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in current)
        {
            if (!ids.Add(value.Id) || !before.TryGetValue(value.Id, out var old) || old.Received < 0 || old.Sent < 0
                || value.Received < old.Received || value.Sent < old.Sent)
                return new(null, null, ResourceMetricState.WarmingUp, current.Count);
            received += value.Received - old.Received;
            sent += value.Sent - old.Sent;
        }
        return new(received / seconds, sent / seconds, ResourceMetricState.Available, current.Count);
    }

    internal static bool TryParseGpuEngine(string instance, double value, out ResourceGpuEngine engine)
    {
        engine = default;
        if (instance.Length > 256 || !double.IsFinite(value) || value < 0) return false;
        // PDH's per-process samples are used transiently, only to sum each physical GPU engine.
        // No process identifier/name is retained in the output or any log.
        var parts = instance.Split('_');
        if (parts.Length < 11 || parts[10].Length == 0 || parts[0] != "pid" || !uint.TryParse(parts[1], out _)
            || parts[2] != "luid" || parts[5] != "phys" || parts[7] != "eng" || parts[9] != "engtype"
            || !TryHex(parts[3], out var high) || !TryHex(parts[4], out var low)
            || !uint.TryParse(parts[6], out var physical) || !uint.TryParse(parts[8], out var number)) return false;
        engine = new(new(high, low, physical), number, value);
        return true;
    }

    private static bool TryHex(string value, out uint number)
    {
        number = 0;
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value.Length is >= 3 and <= 10
            && uint.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number);
    }

    internal static ResourceGpuAggregate GpuUsage(IEnumerable<ResourceGpuEngine> values)
    {
        var engines = new Dictionary<(ResourceGpuAdapter Adapter, uint Engine), double>();
        foreach (var value in values)
        {
            if (!double.IsFinite(value.Percent) || value.Percent < 0)
                return new(ResourceMetric.Missing(ResourceMetricState.Unavailable), null, 0);
            var key = (value.Adapter, value.Engine);
            engines[key] = engines.GetValueOrDefault(key) + value.Percent;
        }
        if (engines.Count == 0) return new(ResourceMetric.Missing(ResourceMetricState.Unavailable), null, 0);
        // Sum processes on the SAME engine, then choose the busiest engine/card. Never sum
        // independent engines or cards into a misleading machine-wide >100% gauge.
        var selected = engines.OrderByDescending(x => x.Value).ThenBy(x => x.Key.Adapter.LuidHigh)
            .ThenBy(x => x.Key.Adapter.LuidLow).ThenBy(x => x.Key.Adapter.PhysicalIndex).First();
        return new(ResourceMetric.Available(Math.Clamp(selected.Value, 0, 100)), selected.Key.Adapter,
            engines.Keys.Select(x => x.Adapter).Distinct().Count());
    }

    internal static ResourceMetric GpuTemperature(uint tenthsCelsius) => tenthsCelsius is > 0 and <= 1500
        ? ResourceMetric.Available(tenthsCelsius / 10d) : ResourceMetric.Missing(ResourceMetricState.Unsupported);
}
