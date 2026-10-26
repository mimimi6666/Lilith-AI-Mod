using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LilithControlCenter;

// No Runtime/model dependency, persistence, subprocess, network request or permission change.
// UI owns cadence/visibility; native calls are always off the UI thread.
internal sealed class WindowsResourceSampler : IResourceSampler
{
    private readonly ResourceProbeSlot<ResourceBasicSample> _basicSlot = new();
    private readonly ResourceProbeSlot<ResourceGpuSample> _gpuSlot = new();
    private readonly IResourceBasicProbe _basic;
    private readonly IResourceGpuProbe _gpu;
    private readonly TimeSpan _budget;
    private long _epoch;
    private int _disposed;

    internal WindowsResourceSampler() : this(new WindowsBasicResourceProbe(), new WindowsGpuResourceProbe(),
        TimeSpan.FromMilliseconds(750)) { }

    internal WindowsResourceSampler(IResourceBasicProbe basic, IResourceGpuProbe gpu, TimeSpan budget)
    {
        _basic = basic;
        _gpu = gpu;
        _budget = budget;
    }

    public void Reset() => Interlocked.Increment(ref _epoch);

    public async Task<ResourceSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0) return ResourceSnapshot.Missing(ResourceMetricState.Unavailable);
        var epoch = Interlocked.Read(ref _epoch);
        // Independent slots ensure a blocked graphics driver cannot prevent CPU/RAM/network display.
        var basicTask = _basicSlot.ReadAsync(() => _basic.Read(epoch), _budget, cancellationToken);
        var gpuTask = _gpuSlot.ReadAsync(() => _gpu.Read(epoch), _budget, cancellationToken);
        await Task.WhenAll(basicTask, gpuTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (epoch != Interlocked.Read(ref _epoch) || Volatile.Read(ref _disposed) != 0)
            return ResourceSnapshot.Missing(ResourceMetricState.Stale);
        var basic = basicTask.Result ?? ResourceBasicSample.Missing(ResourceMetricState.Stale);
        var gpu = gpuTask.Result ?? ResourceGpuSample.Missing(ResourceMetricState.Stale);
        return new(DateTimeOffset.UtcNow, basic.Cpu, gpu.Usage,
            ResourceMetric.Missing(ResourceMetricState.Unsupported), gpu.Temperature,
            basic.Memory, basic.Network, gpu.AdapterCount);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Reset();
        _basicSlot.DisposeWhenIdle(_basic.Dispose);
        _gpuSlot.DisposeWhenIdle(_gpu.Dispose);
    }
}

// One uncancellable native call at most per source. A deadline ends observation, not native
// execution: no replacement workers are spawned until it returns, and late results are discarded.
internal sealed class ResourceProbeSlot<T> where T : class
{
    private readonly object _gate = new();
    private Task<T?>? _pending;
    private bool _disposed;

    internal async Task<T?> ReadAsync(Func<T> read, TimeSpan budget, CancellationToken token)
    {
        Task<T?> task;
        lock (_gate)
        {
            if (_disposed || _pending is { IsCompleted: false }) return null;
            _pending = task = Task.Run<T?>(() =>
            {
                try { return read(); }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { return null; }
            });
        }
        try { return await task.WaitAsync(budget, token).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
    }

    internal void DisposeWhenIdle(Action dispose)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // Disposal can call a driver too. Never synchronously join/close on the UI thread.
            if (_pending is { } pending)
                _ = pending.ContinueWith(_ => DisposeSafely(dispose), CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            else _ = Task.Run(() => DisposeSafely(dispose));
        }
    }

    private static void DisposeSafely(Action dispose)
    {
        try { dispose(); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
    }
}

internal sealed record ResourceBasicSample(ResourceMetric Cpu, ResourceMemory Memory, ResourceNetwork Network)
{
    internal static ResourceBasicSample Missing(ResourceMetricState state) => new(ResourceMetric.Missing(state),
        ResourceMemory.Missing(state), ResourceNetwork.Missing(state));
}

internal sealed record ResourceGpuSample(ResourceMetric Usage, ResourceMetric Temperature, int AdapterCount)
{
    internal static ResourceGpuSample Missing(ResourceMetricState state) =>
        new(ResourceMetric.Missing(state), ResourceMetric.Missing(state), 0);
}

internal interface IResourceBasicProbe : IDisposable { ResourceBasicSample Read(long epoch); }
internal interface IResourceGpuProbe : IDisposable { ResourceGpuSample Read(long epoch); }

internal sealed class WindowsBasicResourceProbe : IResourceBasicProbe
{
    private long _epoch = -1;
    private long _previousTimestamp;
    private ResourceCpuTimes? _cpu;
    private IReadOnlyList<ResourceNetworkCounter>? _network;

    public ResourceBasicSample Read(long epoch)
    {
        if (!OperatingSystem.IsWindows()) return ResourceBasicSample.Missing(ResourceMetricState.Unsupported);
        var now = Stopwatch.GetTimestamp();
        var seconds = _previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalSeconds;
        if (_epoch != epoch || seconds > 10)
        {
            _cpu = null;
            _network = null;
        }
        _epoch = epoch;
        _previousTimestamp = now;
        return new(ReadCpu(), ReadMemory(), ReadNetwork(seconds));
    }

    private ResourceMetric ReadCpu()
    {
        // GetSystemTimes covers only the caller's primary group on >64-processor hosts.
        // Do not label that partial figure as whole-machine CPU utilization.
        if (GetActiveProcessorGroupCount() != 1)
        {
            _cpu = null;
            return ResourceMetric.Missing(ResourceMetricState.Unsupported);
        }
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _cpu = null;
            return ResourceMetric.Missing(ResourceMetricState.Unavailable);
        }
        var current = new ResourceCpuTimes(idle, kernel, user);
        var value = ResourceSampleMath.CpuPercent(_cpu, current);
        _cpu = current;
        return value;
    }

    private static ResourceMemory ReadMemory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? ResourceSampleMath.Memory(memory.TotalPhysical, memory.AvailablePhysical)
            : ResourceMemory.Missing(ResourceMetricState.Unavailable);
    }

    private ResourceNetwork ReadNetwork(double seconds)
    {
        try
        {
            var current = new List<ResourceNetworkCounter>();
            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            if (adapters.Length > 256) { _network = null; return ResourceNetwork.Missing(ResourceMetricState.Unavailable); }
            foreach (var adapter in adapters)
            {
                // Interface traffic, not an Internet speed test. Exclude explicitly typed
                // loopback/tunnels; virtual Ethernet may still include duplicated VPN/bridge traffic.
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet
                        or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet
                        or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT)) continue;
                var stats = adapter.GetIPStatistics();
                current.Add(new(adapter.Id, stats.BytesReceived, stats.BytesSent));
            }
            var sample = ResourceSampleMath.Network(_network, current, seconds);
            _network = current;
            return sample;
        }
        catch (Exception error) when (error is NetworkInformationException or PlatformNotSupportedException
            or InvalidOperationException or System.Security.SecurityException)
        {
            // A partial sum is not an available aggregate. Reset all adapters after a failed read.
            _network = null;
            return ResourceNetwork.Missing(ResourceMetricState.Unavailable);
        }
    }

    public void Dispose() { }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length, MemoryLoad;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern ushort GetActiveProcessorGroupCount();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
