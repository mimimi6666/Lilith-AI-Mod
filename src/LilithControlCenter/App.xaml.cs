using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace LilithControlCenter;

public partial class App : Application
{
    private ControlCenterSingleInstance? _instance;
    private ControlCenterSystemTheme? _systemTheme;
    private readonly HashSet<string> _runtimeStartupFailureCodes = new(StringComparer.Ordinal);
    internal static ControlCenterPreferences UiPreferences { get; set; } =
        ControlCenterPreferences.Default(System.Globalization.CultureInfo.CurrentUICulture.Name);
    internal static string StartupDiagnosticPath { get; } = Path.Combine(
        Path.GetTempPath(),
        "LilithControlCenter-provider-live-startup.txt");

    internal static void TraceStartup(string value)
        => File.AppendAllText(
            StartupDiagnosticPath,
            DateTimeOffset.Now.ToString("O") + " " + value + Environment.NewLine);

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new ControlCenterSingleInstance(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MainWindow is MainWindow window) window.ShowFromTray();
        })));
        if (!_instance.IsPrimary) { Shutdown(); return; }
        var store = ControlCenterPreferenceStore.ForCurrentUser();
        UiPreferences = store.Load(System.Globalization.CultureInfo.CurrentUICulture.Name, out var writable);
        if (writable && UiPreferences.Revision == 0)
        {
            try { UiPreferences = store.Save(UiPreferences, UiPreferences.Language, UiPreferences.ChatSendSoundEnabled); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { /* Preserve storage failure; explicit settings UI reports it. */ }
        }
        ControlCenterLocalization.Current.SetLanguage(UiPreferences.Language);
        _systemTheme = new ControlCenterSystemTheme(this);
        _systemTheme.Refresh();
        File.WriteAllText(StartupDiagnosticPath, string.Empty);
        DispatcherUnhandledException += (_, args) =>
        {
            TraceStartup(
                "DispatcherUnhandledException "
                + args.Exception.GetType().Name
                + " "
                + args.Exception.Message);
            args.Handled = true;
            MessageBox.Show(
                "控制中心已安全停止目前的畫面更新。\n沒有修改遊戲設定或玩家資料。",
                "Lilith Control Center",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var runtimeBootstrap = LilithRuntimeBootstrap.ForCurrentDistribution();
        var runtimeBootstrapReceipt = runtimeBootstrap.EnsureStarted();
        TraceStartup(
            "Runtime bootstrap app.startup "
            + runtimeBootstrapReceipt.Code);
        try
        {
            MainWindow = ProviderControlCenterStartupPolicy
                .EnablesLiveProviderNetwork(e.Args)
                    ? new MainWindow(
                        ProviderControlCenterLiveNetworkBoundary.ForCurrentUser(),
                        runtimeBootstrap)
                    : new MainWindow(runtimeBootstrap);
            TraceStartup("MainWindow constructed");
            MainWindow.Show();
            if (e.Args.Contains("--start-in-tray", StringComparer.Ordinal) && MainWindow is MainWindow startupWindow)
                startupWindow.HideToTray();
            TraceStartup("MainWindow Show returned; visible=" + MainWindow.IsVisible);
        }
        catch (Exception exception)
        {
            TraceStartup("Startup exception " + exception);
            MessageBox.Show(
                $"Provider 實測模式初始化失敗：{exception.GetType().Name}\n{exception.Message}",
                "Lilith Control Center",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            MainWindow = new MainWindow(runtimeBootstrap);
            MainWindow.Show();
            TraceStartup("Fallback MainWindow shown; visible=" + MainWindow.IsVisible);
        }
        _ = ObserveRuntimeStartupAsync(runtimeBootstrapReceipt);
    }

    internal async Task ObserveRuntimeStartupAsync(LilithRuntimeBootstrapReceipt receipt)
    {
        var code = receipt.Code;
        var failed = !receipt.LaunchWasRequested;
        int? exitCode = null;
        if (receipt.StartupObservation is { } observation)
        {
            var result = await observation.ConfigureAwait(false);
            code = result.Code;
            failed = result.Failed;
            exitCode = result.ExitCode;
        }
        var diagnostic = RuntimeStartupDiagnosticPresentation.Create(code, exitCode,
            DateTimeOffset.UtcNow, typeof(App).Assembly.GetName().Version);
        code = diagnostic.Code;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                try { TraceStartup(diagnostic.ToSafeLogLine()); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                // Every launch is observed, including a Workbench bootstrap
                // that wins the single-instance race. Show each safe failure
                // code once instead of stacking duplicate launch warnings.
                if (!failed || MainWindow is null || !_runtimeStartupFailureCodes.Add(code)) return;
                new RuntimeStartupDiagnosticDialog(diagnostic) { Owner = MainWindow }.ShowDialog();
            });
        }
        catch (OperationCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            // Closing the app cancels queued UI work, not the Runtime process.
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is MainWindow window) window.AllowSystemShutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _systemTheme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        _systemTheme?.Refresh();
        base.OnActivated(e);
    }

    internal static void RefreshUiTheme()
    {
        if (Current is App app && app._systemTheme is not null)
            app._systemTheme.Refresh();
        else if (Current is { } isolatedApplication)
        {
            // Isolated presentation fixtures do not start the product lifecycle.
            new ControlCenterThemeSelection(() => UiPreferences,
                ControlCenterSystemTheme.ReadSystemDarkMode,
                dark => ControlCenterTheme.Current.Apply(isolatedApplication, dark)).Refresh();
        }
    }
}
