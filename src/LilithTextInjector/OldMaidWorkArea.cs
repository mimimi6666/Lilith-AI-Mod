using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LilithTextInjector;

// Read-only screen/work-area conversion. Never changes window regions/styles.
internal static class OldMaidWorkArea
{
    internal static Rect Read()
    {
        var fallback = new Rect(8, 56, Math.Max(1, Screen.width - 16), Math.Max(1, Screen.height - 64));
        if (!OperatingSystem.IsWindows()) return fallback;
        try
        {
            var window = PlatformService.Current.GetAppWindowHandle();
            if (window == IntPtr.Zero || !GetClientRect(window, out var client)) return fallback;
            var origin = new Point();
            if (!ClientToScreen(window, ref origin)) return fallback;
            var monitor = MonitorFromWindow(window, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return fallback;
            var cw = client.Right - client.Left;
            var ch = client.Bottom - client.Top;
            if (cw <= 0 || ch <= 0) return fallback;
            var sx = (float)Screen.width / cw;
            var sy = (float)Screen.height / ch;
            var left = Math.Clamp((info.Work.Left - origin.X) * sx, 0, Screen.width);
            var right = Math.Clamp((info.Work.Right - origin.X) * sx, left, Screen.width);
            var top = Math.Clamp(Screen.height - (info.Work.Top - origin.Y) * sy, 0, Screen.height);
            var bottom = Math.Clamp(Screen.height - (info.Work.Bottom - origin.Y) * sy, 0, top);
            return right - left < 32 || top - bottom < 32 ? fallback
                : new Rect(left + 8, bottom + 8, right - left - 16, top - bottom - 16);
        }
        catch { return fallback; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { internal int Size; internal NativeRect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);
}
