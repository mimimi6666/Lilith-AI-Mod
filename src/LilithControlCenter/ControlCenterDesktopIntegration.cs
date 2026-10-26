using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using LilithRuntimeContracts;
using Microsoft.Win32;

namespace LilithControlCenter;

internal sealed class ControlCenterSingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _show;
    private readonly RegisteredWaitHandle? _wait;
    internal bool IsPrimary { get; }

    internal ControlCenterSingleInstance(Action show)
    {
        var user = WindowsIdentity.GetCurrent().User!.Value;
        var executable = Environment.ProcessPath!;
        _mutex = new Mutex(true, LilithDesktopLifecycle.SignalName("control-center-instance", user, executable), out var primary);
        IsPrimary = primary;
        var name = LilithDesktopLifecycle.SignalName("control-center-show", user, executable);
        if (primary)
        {
            _show = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => show(), null, Timeout.Infinite, false);
        }
        else
        {
            // A simultaneous launch can arrive before the first window's event.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (EventWaitHandle.TryOpenExisting(name, out var existing))
                { using (existing) existing.Set(); break; }
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _show?.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

internal static class ControlCenterStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LilithControlCenter";
    internal static string Command(string executable) => "\"" + Path.GetFullPath(executable) + "\" --start-in-tray";
    internal static bool IsEnabled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command(executable), StringComparison.OrdinalIgnoreCase);
    }
    internal static void SetEnabled(string executable, bool enabled)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new IOException("Startup executable missing.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        var before = key.GetValue(ValueName) as string;
        // Never replace another installation/command sharing the registry name.
        if (before is not null && !string.Equals(before, Command(executable), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Startup registration belongs to a different installation.");
        if (enabled) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        if (IsEnabled(executable) != enabled) throw new IOException("Startup registration readback failed.");
    }
}

internal static class ControlCenterRuntimeExit
{
    internal static async Task<bool> StopAsync()
    {
        var executable = Path.Combine(LilithRuntimeBootstrap.ResolveRuntimeDirectory(AppContext.BaseDirectory), "LilithRuntime.exe");
        var expected = new List<Process>();
        foreach (var process in Process.GetProcessesByName("LilithRuntime"))
        {
            try
            {
                if (process.SessionId == Process.GetCurrentProcess().SessionId
                    && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    expected.Add(process);
                else process.Dispose();
            }
            catch { process.Dispose(); }
        }
        try
        {
            if (expected.Count == 0) return true;
            var name = LilithDesktopLifecycle.SignalName("runtime-exit", WindowsIdentity.GetCurrent().User!.Value, executable);
            if (!EventWaitHandle.TryOpenExisting(name, out var signal)) return false;
            using (signal) signal.Set();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await Task.WhenAll(expected.Select(p => p.WaitForExitAsync(deadline.Token)));
            return true;
        }
        catch (Exception e) when (e is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
        finally { foreach (var process in expected) process.Dispose(); }
    }
}
