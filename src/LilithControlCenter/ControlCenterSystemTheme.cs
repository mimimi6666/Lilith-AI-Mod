using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LilithControlCenter;

// Read-only OS presentation notifications, independent of Runtime connectivity.
internal sealed class ControlCenterSystemTheme : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ControlCenterThemeSelection _selection;
    private volatile bool _disposed;
    private int _refreshQueued;

    internal ControlCenterSystemTheme(Application application, Func<bool?>? systemDarkMode = null)
    {
        _dispatcher = application.Dispatcher;
        _selection = new ControlCenterThemeSelection(() => App.UiPreferences, systemDarkMode ?? ReadSystemDarkMode,
            dark => ControlCenterTheme.Current.Apply(application, dark));
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    internal void Refresh()
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        _selection.Refresh();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        // Notifications can arrive off-thread or in bursts. Read the latest
        // preference on the UI thread, not a captured auto/manual state.
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished
            || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                Refresh();
            }));
        }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        { Interlocked.Exchange(ref _refreshQueued, 0); }
    }

    internal static bool? ReadSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return ControlCenterThemeSelection.ReadDarkModeValue(key?.GetValue("AppsUseLightTheme"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { return null; }
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
