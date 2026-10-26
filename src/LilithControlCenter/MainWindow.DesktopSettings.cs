using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LilithControlCenter;

public partial class MainWindow
{
    private Forms.NotifyIcon? _trayIcon;
    private ContextMenu? _trayMenu;
    private bool _exitRequested;
    private bool _systemShutdown;
    private bool _updatingDesktopSettings;
    private static ControlCenterLocalization Texts => ControlCenterLocalization.Current;

    private void InitializeDesktopSettings()
    {
        InterfaceLanguageSelector.ItemsSource = new[]
        {
            new UiLanguageOption("zh-TW", "繁體中文"), new UiLanguageOption("zh-CN", "简体中文"),
            new UiLanguageOption("ja-JP", "日本語"), new UiLanguageOption("en-US", "English"),
            new UiLanguageOption("ru-RU", "Русский")
        };
        Texts.LanguageChanged += RefreshDesktopLanguage;
        RefreshDesktopSettings();
        // A test-created Window has no product App owner and must not register a tray icon.
        if (Application.Current is not App) return;
        try
        {
            using var image = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/lilith-control-center-icon.png"))!.Stream;
            using var bitmap = new System.Drawing.Bitmap(image);
            var handle = bitmap.GetHicon();
            System.Drawing.Icon icon;
            try { using var borrowed = System.Drawing.Icon.FromHandle(handle); icon = (System.Drawing.Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
            _trayIcon = new Forms.NotifyIcon { Icon = icon, Text = "Lilith Control Center", Visible = true };
            _trayIcon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowFromTray); };
            _trayIcon.MouseUp += (_, e) =>
            {
                if (e.Button != Forms.MouseButtons.Right || (_shutdownStarted && !_playerSavePrepared)) return;
                Dispatcher.Invoke(() =>
                {
                    _trayMenu = CreateTrayMenu();
                    // Give the popup a foreground owner so outside clicks dismiss it.
                    var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    SetForegroundWindow(handle);
                    _trayMenu.IsOpen = true;
                });
            };
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { App.TraceStartup("Tray initialization failed: " + e.GetType().Name); }
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
        menu.SetResourceReference(StyleProperty, "DarkContextMenuStyle");
        var open = new MenuItem { Header = Texts["tray.open"] };
        open.SetResourceReference(StyleProperty, "TrayMenuItemStyle");
        open.Click += (_, _) => ShowFromTray();
        var chat = new MenuItem { Header = Texts["tray.chat"] };
        chat.IsEnabled = !_playerSavePrepared;
        chat.SetResourceReference(StyleProperty, "TrayMenuItemStyle");
        chat.Click += (_, _) => { ShowFromTray(); OpenWorkbench_Click(this, new RoutedEventArgs()); };
        var exit = new MenuItem { Header = Texts["tray.exit"] };
        exit.IsEnabled = !_playerSaveBusy;
        exit.SetResourceReference(StyleProperty, "TrayMenuItemStyle");
        exit.Click += (_, _) => RequestExit();
        var separator = new Separator();
        separator.SetResourceReference(StyleProperty, "DarkMenuSeparatorStyle");
        menu.Items.Add(open); menu.Items.Add(chat); menu.Items.Add(separator); menu.Items.Add(exit);
        return menu;
    }

    internal void ShowFromTray()
    {
        if (_shutdownStarted && !_playerSavePrepared) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal bool HideToTray()
    {
        if (_trayIcon is not { Visible: true }) return false;
        _hermesWorkbenchWindow?.Hide();
        Hide();
        return true;
    }

    internal void RequestExit() { _exitRequested = true; Close(); }
    internal void AllowSystemShutdown()
    {
        _systemShutdown = true;
        _exitRequested = true;
        DisposeDesktopIntegration();
        _ = ControlCenterRuntimeExit.StopAsync();
    }

    private void RefreshDesktopLanguage()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshDesktopLanguage); return; }
        RefreshDesktopSettings();
        _nativeSettings?.RefreshLanguage();
        RenderHermesDefaultModelCard();
        RenderLettersStatus();
        RenderManualNoteSettingsStatus();
        UpdateHermesCredentialControls();
        if (_telegramStatus is not null && _telegramStatusAuthoritative)
            ApplyTelegramStatus(_telegramStatus);
        TelegramStatusText.Text = Texts.RefreshOwnedUiLabel(TelegramStatusText.Text);
        HeaderBreadcrumb.Text = Texts["nav." + _pages.First(p => p.Value.Visibility == Visibility.Visible).Key.ToLowerInvariant()];
    }

    private void RefreshDesktopSettings()
    {
        _updatingDesktopSettings = true;
        try
        {
            var preferences = App.UiPreferences;
            InterfaceLanguageSelector.SelectedValue = preferences.Language;
            ChatSendSoundCheckBox.IsChecked = preferences.ChatSendSoundEnabled;
            ThemeModeSelector.SelectedValue = preferences.FollowSystemTheme
                ? "system" : preferences.DarkModeEnabled ? "dark" : "light";
            StartupCheckBox.IsChecked = ControlCenterStartupRegistration.IsEnabled(Environment.ProcessPath!);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { DesktopSettingsStatus.Text = Texts["settings.failed"]; }
        finally { _updatingDesktopSettings = false; }
    }

    private void InterfaceLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDesktopSettings || InterfaceLanguageSelector.SelectedValue is not string language) return;
        SaveUiSettings(language, ChatSendSoundCheckBox.IsChecked == true);
    }
    private void ChatSendSound_Click(object sender, RoutedEventArgs e)
        => SaveUiSettings(App.UiPreferences.Language, ChatSendSoundCheckBox.IsChecked == true);
    private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDesktopSettings || ThemeModeSelector.SelectedValue is not string mode
            || mode is not ("system" or "light" or "dark")) return;
        SaveUiSettings(App.UiPreferences.Language, App.UiPreferences.ChatSendSoundEnabled,
            darkMode: mode == "system" ? null : mode == "dark", followSystemTheme: mode == "system");
    }
    private void SaveUiSettings(string language, bool sound, bool? darkMode = null, bool? followSystemTheme = null)
    {
        if (_updatingDesktopSettings) return;
        try
        {
            App.UiPreferences = ControlCenterPreferenceStore.ForCurrentUser().Save(App.UiPreferences, language, sound,
                darkMode, followSystemTheme);
            if (darkMode.HasValue || followSystemTheme.HasValue)
                App.RefreshUiTheme();
            if (!sound) _hermesWorkbenchWindow?.StopSendCue();
            Texts.SetLanguage(language);
            DesktopSettingsStatus.Text = Texts["settings.saved"];
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { RefreshDesktopSettings(); DesktopSettingsStatus.Text = Texts["settings.failed"]; }
    }
    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingDesktopSettings) return;
        try
        {
            ControlCenterStartupRegistration.SetEnabled(Environment.ProcessPath!, StartupCheckBox.IsChecked == true);
            DesktopSettingsStatus.Text = Texts["settings.saved"];
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { RefreshDesktopSettings(); DesktopSettingsStatus.Text = Texts["settings.startupFailed"]; }
    }

    private void DisposeDesktopIntegration()
    {
        Texts.LanguageChanged -= RefreshDesktopLanguage;
        if (_trayMenu is not null) _trayMenu.IsOpen = false;
        if (_trayIcon is not null)
        { _trayIcon.Visible = false; _trayIcon.Icon?.Dispose(); _trayIcon.Dispose(); _trayIcon = null; }
    }

    private sealed record UiLanguageOption(string Code, string Label);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
