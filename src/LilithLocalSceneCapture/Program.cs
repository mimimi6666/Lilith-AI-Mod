using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace LilithLocalSceneCapture;

// Read-only collector. The native host owns explicit opt-in and a kill-on-close
// Job. This helper has no input-control, network, provider, history or settings API.
internal static partial class Program
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "1password", "bitwarden", "keepass", "keepassxc", "lastpass", "authy",
        "credentialuibroker", "credentialui", "consent", "logonui", "winlogon",
        "lilith", "lilithcontrolcenter", "lilithruntime", "lilithvoicehost"
    };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--probe")
            {
                Console.WriteLine("{\"schema\":\"local_scene_capture_probe_v1\",\"screenshot_taken\":false,\"ui_text_read\":false}");
                return 0;
            }
            if (args.Length == 3 && args[0] == "--select" && args[1] == "--trigger" &&
                args[2] is "player_share" or "proactive")
            {
                var selected = SelectTarget(args[2]);
                Console.WriteLine(JsonSerializer.Serialize(new { schema = "local_scene_target_v1", status = "ready",
                    window = new { handle = selected.Handle, key = selected.Key, process = selected.Process, bounds = selected.Bounds } }));
                return 0;
            }
            if (args.Length != 9 || args[0] != "--capture" || args[1] != "--expected-window" ||
                args[3] != "--expected-key" || args[5] != "--out" ||
                args[7] != "--trigger" || args[8] is not ("player_share" or "proactive") ||
                !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected <= 0 ||
                !Regex.IsMatch(args[4], "^[a-f0-9]{64}$"))
                return 2;
            Capture(new IntPtr(expected), args[4], args[6], args[8]);
            return 0;
        }
        catch (SceneCaptureFailure failure)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "local_scene_capture_error_v1", status = "unavailable", code = failure.Code }));
            return 3;
        }
        catch
        {
            // No title, UI text, file path or screenshot contents in ordinary logs.
            Console.WriteLine("{\"schema\":\"local_scene_capture_error_v1\",\"status\":\"unavailable\",\"code\":\"capture_unavailable\"}");
            return 3;
        }
    }

    private static void Capture(IntPtr handle, string expectedKey, string output, string trigger)
    {
        var target = Path.GetFullPath(output);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Fresh output required");
        for (var ancestor = Directory.GetParent(target); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked capture directory");
        var prior = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            CheckWindow(handle, trigger);
            var (key, process) = Identity(handle);
            if (key != expectedKey) throw new SceneCaptureFailure("target_changed");
            if (Sensitive.Contains(process)) throw new SceneCaptureFailure("sensitive_window");
            var rect = Bounds(handle);
            int width = rect[2] - rect[0], height = rect[3] - rect[1];
            if (width < 96 || height < 96 || (long)width * height > 25_000_000) throw new InvalidOperationException("Capture size");
            // Required UIA read: failure/hang is unavailable, never permission to
            // skip password detection. Host timeout terminates only this Job.
            UiResult ui;
            try { ui = ReadUi(handle, process, rect); }
            catch { throw new SceneCaptureFailure("ui_unavailable"); }
            if (ui.Password) throw new SceneCaptureFailure("sensitive_ui");
            CheckSame(handle, key, rect, trigger);
            Directory.CreateDirectory(target);
            var captured = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var imagePath = Path.Combine(target, "window.png");
            using (var bitmap = new Bitmap(width, height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    var dc = graphics.GetHdc();
                    try
                    {
                        // Client pixels of the selected visible window, excluding
                        // Lilith's overlay. PrintWindow can block: host Job/timeout
                        // bounds the entire helper. No full-desktop fallback.
                        if (!Native.PrintWindow(handle, dc, 3)) throw new SceneCaptureFailure("capture_backend_unavailable");
                    }
                    finally { graphics.ReleaseHdc(dc); }
                    foreach (var mask in ui.Masks)
                        graphics.FillRectangle(Brushes.Black, (float)mask[0], (float)mask[1],
                            (float)(mask[2] - mask[0]), (float)(mask[3] - mask[1]));
                }
                if (UniformPixels(bitmap)) throw new SceneCaptureFailure("blank_capture");
                bitmap.Save(imagePath, ImageFormat.Png);
            }
            CheckSame(handle, key, rect, trigger);
            using var image = File.OpenRead(imagePath);
            var digest = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
            var source = new Dictionary<string, object?>
            {
                ["window"] = new { status = "available", window_key = key, observed_at = captured,
                    data = new { application = process, title = Bounded(Title(handle), 512), url = ui.Url,
                        client_rect_screen_px = rect, dpi = Native.GetDpiForWindow(handle), is_foreground = Native.GetForegroundWindow() == handle } },
                ["ui"] = new { status = "available", window_key = key, observed_at = captured,
                    data = new { nodes = ui.Nodes, truncated = ui.Truncated, provider = "Windows_UIA_visible_control_view" } },
                ["activity"] = Activity(key, captured, Native.GetForegroundWindow() == handle),
                ["media"] = new { status = "unavailable", data = (object?)null }
            };
            var signals = new
            {
                schema = "lilith_capture_signals_v1",
                frame = new { image_sha256 = digest, width, height, captured_at = captured, window_key = key },
                sources = source, collection = "explicit_one_shot_visible_content_window",
                target_selection = Native.GetForegroundWindow() == handle ? "foreground" : "visible_beneath_lilith",
                continuous_observer = false, arbitrary_input_values_collected = false, known_input_masks_applied = true,
                privacy = "Visible secrets are not guaranteed redacted. Do not enable on sensitive screens."
            };
            using var file = new FileStream(Path.Combine(target, "signals.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(file, signals);
            file.Flush(true);
        }
        finally { if (prior != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(prior); }
    }

    private sealed class UiResult
    {
        internal readonly List<object> Nodes = new();
        internal readonly List<double[]> Masks = new();
        internal bool Password;
        internal bool Truncated;
        internal string? Url;
    }

    private static UiResult ReadUi(IntPtr handle, string process, int[] rect)
    {
        var result = new UiResult();
        var pending = new Queue<AutomationElement>();
        pending.Enqueue(AutomationElement.FromHandle(handle) ?? throw new InvalidOperationException("UI unavailable"));
        var walker = TreeWalker.ControlViewWalker;
        var watch = Stopwatch.StartNew();
        int visited = 0;
        while (pending.Count > 0 && visited < 1500 && result.Nodes.Count < 256 && watch.ElapsedMilliseconds < 1800)
        {
            var element = pending.Dequeue();
            visited++;
            try
            {
                var current = element.Current;
                if (current.IsOffscreen) continue;
                if (current.IsPassword) { result.Password = true; continue; }
                var bounds = current.BoundingRectangle;
                double left = Math.Max(rect[0], bounds.Left), top = Math.Max(rect[1], bounds.Top);
                double right = Math.Min(rect[2], bounds.Right), bottom = Math.Min(rect[3], bounds.Bottom);
                if (right <= left || bottom <= top) continue;
                var box = new[] { left - rect[0], top - rect[1], right - rect[0], bottom - rect[1] };
                if (current.ControlType == ControlType.Edit)
                {
                    result.Masks.Add(box);
                    if ((process is "chrome" or "msedge" or "firefox") &&
                        (current.AutomationId == "addressEditBox" || current.Name is "Address and search bar" or "網址列與搜尋列" or "地址和搜索栏" or "地址和搜索欄") &&
                        element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                        result.Url = SafeUrl(((ValuePattern)pattern).Current.Value);
                }
                else
                {
                    string? role = current.ControlType.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal) switch
                    {
                        "Text" => "text", "Hyperlink" => "link", "Button" => "button", "Image" => "image",
                        "Document" => "document", "List" => "list", "Group" => "group", "Pane" => "pane",
                        "TabItem" => "tab", "ToolBar" => "navigation", _ => null
                    };
                    var text = Bounded(current.Name ?? "", 768);
                    if (role is not null && text.Length > 0)
                        result.Nodes.Add(new { id = "uia-" + result.Nodes.Count.ToString(CultureInfo.InvariantCulture), role, text,
                            box_px = box, visible = true, sensitive = false, focused = current.HasKeyboardFocus });
                }
                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                {
                    if (pending.Count >= 1500 || watch.ElapsedMilliseconds >= 1800) break;
                    pending.Enqueue(child);
                }
            }
            catch (ElementNotAvailableException) { }
        }
        result.Truncated = pending.Count > 0;
        return result;
    }

    private static string? SafeUrl(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            return null;
        var parts = uri.AbsolutePath.Split('/');
        for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 96) parts[i] = "[opaque]";
        var path = string.Join('/', parts);
        if (path.Length > 300) path = path[..300];
        return uri.GetLeftPart(UriPartial.Authority) + path;
    }

    private static string Bounded(string text, int limit)
    {
        text = Regex.Replace(text, "https?://[^\\s|<>\"\\u3000]+", match => SafeUrl(match.Value) ?? "[redacted URL]");
        return text.Length <= limit ? text : text[..limit];
    }

    private static object Activity(string key, string captured, bool isForeground)
    {
        var input = new Native.LastInput { Size = (uint)Marshal.SizeOf<Native.LastInput>() };
        if (!Native.GetLastInputInfo(ref input)) return new { status = "unavailable", data = (object?)null };
        return new { status = "available", window_key = key, observed_at = captured,
            data = new { idle_seconds = unchecked((uint)Environment.TickCount - input.Tick) / 1000.0, is_foreground = isForeground } };
    }

    private static void CheckWindow(IntPtr handle, string trigger)
    {
        if (SelectTarget(trigger).Handle != handle.ToInt64()) throw new SceneCaptureFailure("target_changed");
    }

    private static (string Key, string Process) Identity(IntPtr handle)
    {
        Native.GetWindowThreadProcessId(handle, out var pid);
        using var process = Process.GetProcessById(checked((int)pid));
        var data = FormattableString.Invariant($"{handle.ToInt64()}/{pid}/{process.StartTime.ToUniversalTime().Ticks}");
        return (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant(), process.ProcessName.ToLowerInvariant());
    }

    private static int[] Bounds(IntPtr handle)
    {
        if (!Native.GetClientRect(handle, out var rect)) throw new InvalidOperationException("Geometry");
        var point = new Native.Point();
        if (!Native.ClientToScreen(handle, ref point)) throw new InvalidOperationException("Origin");
        return new[] { point.X, point.Y, point.X + rect.R - rect.L, point.Y + rect.B - rect.T };
    }

    private static void CheckSame(IntPtr handle, string key, int[] rect, string trigger)
    {
        CheckWindow(handle, trigger);
        if (Identity(handle).Key != key || !Bounds(handle).AsSpan().SequenceEqual(rect))
            throw new SceneCaptureFailure("target_changed");
    }

    private static string Title(IntPtr handle)
    {
        var text = new StringBuilder(1024);
        Native.GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct LastInput { internal uint Size, Tick; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr handle, ref Point point);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr handle, StringBuilder text, int length);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LastInput value);
    }
}
