using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace LilithLocalSceneCapture;

internal static partial class Program
{
    private static SceneWindow WindowSnapshot(IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new SceneCaptureFailure("no_content_window");
        var visible = Native.IsWindowVisible(handle);
        var minimized = Native.IsIconic(handle);
        if (!visible || minimized)
            return new SceneWindow(handle.ToInt64(), "", "", new int[4], 0, visible, minimized);
        if (Native.DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) != 0)
            throw new SceneCaptureFailure("target_identity_unavailable");
        if (cloaked != 0) return new SceneWindow(handle.ToInt64(), "", "", new int[4], 0, Cloaked: true);
        var rect = Bounds(handle);
        var monitor = Native.MonitorFromWindow(handle, 0).ToInt64();
        var className = new StringBuilder(256);
        Native.GetClassName(handle, className, className.Capacity);
        bool shell = className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        var style = Native.GetWindowLongPtr(handle, -20).ToInt64();
        if (shell) return new SceneWindow(handle.ToInt64(), "", "", rect, monitor, Shell: true);
        var (key, process) = Identity(handle);
        return new SceneWindow(handle.ToInt64(), key, process, rect, monitor, ToolWindow: (style & 0x80) != 0);
    }

    private static SceneWindow SelectTarget(string trigger)
    {
        var prior = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var foreground = Native.GetForegroundWindow();
            var first = WindowSnapshot(foreground);
            IEnumerable<SceneWindow> Ordered()
            {
                var seen = new HashSet<IntPtr>();
                // Begin immediately below our foreground UI, not at unrelated
                // always-on-top windows above it. Never search past the first
                // eligible external content window.
                for (var handle = Native.GetWindow(foreground, 2); handle != IntPtr.Zero; handle = Native.GetWindow(handle, 2))
                {
                    if (!seen.Add(handle) || seen.Count > 512) throw new SceneCaptureFailure("target_selection_limit");
                    if (!Native.IsWindowVisible(handle) || Native.IsIconic(handle) ||
                        Native.MonitorFromWindow(handle, 0).ToInt64() != first.Monitor) continue;
                    yield return WindowSnapshot(handle);
                }
            }
            var selected = SceneTargetPolicy.Choose(first, Ordered(), trigger);
            if (Native.GetForegroundWindow() != foreground) throw new SceneCaptureFailure("target_changed");
            return selected;
        }
        finally { if (prior != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(prior); }
    }

    private static bool UniformPixels(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0).ToArgb();
        for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 32))
            for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 32))
                if (bitmap.GetPixel(x, y).ToArgb() != first) return false;
        return true;
    }

    private static partial class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr handle, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr handle, StringBuilder text, int length);
        [DllImport("user32.dll")] internal static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out int value, int size);
    }
}
