using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LilithControlCenter;

// Uses Windows' existing graphics counters/driver interface, never a bundled driver,
// administrator elevation, shell command, vendor executable or downloaded dependency.
internal sealed class WindowsGpuResourceProbe : IResourceGpuProbe
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFormatDouble = 0x200;
    private const int MaximumCounterBytes = 2 * 1024 * 1024;
    private IntPtr _query;
    private IntPtr _counter;
    private long _epoch = -1;
    private long _previousTimestamp;
    private long _retryAfter;
    private bool _primed;

    public ResourceGpuSample Read(long epoch)
    {
        if (!OperatingSystem.IsWindows()) return ResourceGpuSample.Missing(ResourceMetricState.Unsupported);
        var now = Stopwatch.GetTimestamp();
        if (_epoch != epoch || (_previousTimestamp != 0 && Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalSeconds > 10))
        {
            CloseQuery();
            _retryAfter = 0;
        }
        _epoch = epoch;
        _previousTimestamp = now;
        if (now < _retryAfter) return ResourceGpuSample.Missing(ResourceMetricState.Unsupported);
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQueryW(null, UIntPtr.Zero, out _query) != 0
                    || PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", UIntPtr.Zero, out _counter) != 0)
                    return BackOff(now);
            }
            if (PdhCollectQueryData(_query) != 0) return BackOff(now);
            if (!_primed)
            {
                _primed = true;
                return ResourceGpuSample.Missing(ResourceMetricState.WarmingUp);
            }
            var engines = ReadEngines();
            if (engines is null) return ResourceGpuSample.Missing(ResourceMetricState.Unavailable);
            var aggregate = ResourceSampleMath.GpuUsage(engines);
            var temperature = aggregate.Selected is { } selected ? ReadTemperature(selected)
                : ResourceMetric.Missing(ResourceMetricState.Unsupported);
            return new(aggregate.Usage, temperature, aggregate.AdapterCount);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or System.Security.SecurityException)
        { return BackOff(now); }
    }

    private ResourceGpuSample BackOff(long now)
    {
        CloseQuery();
        _retryAfter = now + 30 * Stopwatch.Frequency;
        return ResourceGpuSample.Missing(ResourceMetricState.Unsupported);
    }

    private IReadOnlyList<ResourceGpuEngine>? ReadEngines()
    {
        uint bytes = 0, count = 0;
        if (PdhGetFormattedCounterArrayW(_counter, PdhFormatDouble, ref bytes, out count, IntPtr.Zero) != PdhMoreData
            || bytes == 0 || bytes > MaximumCounterBytes || count == 0 || count > 32768) return null;
        var allocated = checked((int)bytes);
        var buffer = Marshal.AllocHGlobal(allocated);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, PdhFormatDouble, ref bytes, out count, buffer) != 0
                || bytes > allocated || count == 0) return null;
            var stride = Marshal.SizeOf<PdhCounterItem>();
            if ((ulong)count * (ulong)stride > bytes) return null;
            var engines = new List<ResourceGpuEngine>((int)count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<PdhCounterItem>(IntPtr.Add(buffer, checked(index * stride)));
                if (item.Value.Status is not (0 or 1)) return null;
                var offset = item.Name.ToInt64() - buffer.ToInt64();
                if (offset < 0 || offset >= bytes || (offset & 1) != 0) return null;
                var maxChars = Math.Min(257, ((int)bytes - (int)offset) / 2);
                var length = 0;
                while (length < maxChars && Marshal.ReadInt16(item.Name, length * 2) != 0) length++;
                if (length == maxChars) return null;
                var name = Marshal.PtrToStringUni(item.Name, length) ?? string.Empty;
                if (!ResourceSampleMath.TryParseGpuEngine(name, item.Value.Value, out var engine)) return null;
                engines.Add(engine);
            }
            return engines;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static ResourceMetric ReadTemperature(ResourceGpuAdapter adapter)
    {
        var opened = new OpenAdapter
        {
            LuidLow = adapter.LuidLow,
            LuidHigh = unchecked((int)adapter.LuidHigh)
        };
        try
        {
            if (D3DKMTOpenAdapterFromLuid(ref opened) < 0 || opened.Handle == 0)
                return ResourceMetric.Missing(ResourceMetricState.Unsupported);
            try
            {
                // SDK d3dkmthk.h: KMTQAITYPE_ADAPTERPERFDATA = 62; temperature is 0.1 C.
                // Use the SAME LUID/physical adapter as the gauge, not a different/hottest card.
                var data = new AdapterPerformanceData { PhysicalAdapterIndex = adapter.PhysicalIndex };
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AdapterPerformanceData>());
                try
                {
                    Marshal.StructureToPtr(data, pointer, false);
                    var query = new QueryAdapterInfo
                    {
                        Handle = opened.Handle,
                        Type = 62,
                        Data = pointer,
                        DataSize = (uint)Marshal.SizeOf<AdapterPerformanceData>()
                    };
                    if (D3DKMTQueryAdapterInfo(ref query) < 0)
                        return ResourceMetric.Missing(ResourceMetricState.Unsupported);
                    return ResourceSampleMath.GpuTemperature(Marshal.PtrToStructure<AdapterPerformanceData>(pointer).Temperature);
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            finally
            {
                var close = new CloseAdapter { Handle = opened.Handle };
                D3DKMTCloseAdapter(ref close);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or System.Security.SecurityException)
        { return ResourceMetric.Missing(ResourceMetricState.Unsupported); }
    }

    public void Dispose() => CloseQuery();

    private void CloseQuery()
    {
        var query = _query;
        _query = _counter = IntPtr.Zero;
        _primed = false;
        if (query != IntPtr.Zero) PdhCloseQuery(query);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhCounterValue
    {
        [FieldOffset(0)] internal uint Status;
        [FieldOffset(8)] internal double Value;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhCounterItem { internal IntPtr Name; internal PdhCounterValue Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter { internal uint LuidLow; internal int LuidHigh; internal uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter { internal uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo { internal uint Handle, Type; internal IntPtr Data; internal uint DataSize; }
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct AdapterPerformanceData
    {
        [FieldOffset(0)] internal uint PhysicalAdapterIndex;
        [FieldOffset(8)] internal ulong MemoryFrequency;
        [FieldOffset(16)] internal ulong MaxMemoryFrequency;
        [FieldOffset(24)] internal ulong MaxMemoryFrequencyOc;
        [FieldOffset(32)] internal ulong MemoryBandwidth;
        [FieldOffset(40)] internal ulong PcieBandwidth;
        [FieldOffset(48)] internal uint FanRpm;
        [FieldOffset(52)] internal uint Power;
        [FieldOffset(56)] internal uint Temperature;
        [FieldOffset(60)] internal byte PowerStateOverride;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? source, UIntPtr data, out IntPtr query);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes,
        out uint count, IntPtr values);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter adapter);
}
