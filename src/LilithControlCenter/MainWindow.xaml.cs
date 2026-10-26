using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LilithProviderControlPlane;

namespace LilithControlCenter;

public partial class MainWindow : Window
{
    private static readonly TimeSpan OwnedResourceShutdownTimeout =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HermesCredentialReconnectTimeout =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HermesCredentialReconnectRetryDelay =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HermesCredentialHostRetireTimeout =
        TimeSpan.FromSeconds(1);
    private int _refreshing;
    private readonly UnifiedProviderControlPlaneStore _providerStore;
    private readonly DeepSeekControlPlaneStore _legacyDeepSeekStore;
    private readonly D045AiFunctionRoutingStore _aiFunctionRoutingStore;
    private readonly D045WorkbenchModelSelectionCoordinator
        _workbenchModelCoordinator;
    private readonly ILilithRuntimeBootstrap _runtimeBootstrap;
    private readonly IProviderControlCenterNetworkBoundary _providerNetworkBoundary;
    private readonly bool _providerNetworkActionsEnabled;
    private readonly CancellationTokenSource _providerLifetime = new();
    private readonly Dictionary<string, ProviderControlCenterCachedHealthObservation>
        _providerHealthByInstance = new(StringComparer.Ordinal);
    private AiFunctionsPageProjection _aiFunctionsProjection =
        AiFunctionsPageProjection.Build(null, DateTimeOffset.UtcNow);
    private ProviderControlProfile? _selectedProviderProfile;
    private string _selectedConnectedInstanceId = string.Empty;
    private readonly Dictionary<string, Grid> _pages;
    private readonly Dictionary<string, Button> _navigation;
    private readonly DispatcherTimer _liveTelemetryTimer;
    private D046HermesWorkbenchWindow? _hermesWorkbenchWindow;
    private D046HermesWorkbenchHost? _hermesWorkbenchHost;
    private D046HermesFaultRecoveryDraft? _hermesFaultRecoveryDraft;
    private bool _hermesCredentialBusy;
    private bool _hermesCredentialReading;
    private bool _hermesCredentialCatalogAuthoritative;
    private bool _telegramMessagingBusy;
    private bool _telegramMessagingReading;
    private bool _telegramStatusAuthoritative;
    private HermesTelegramStatus? _telegramStatus;
    private bool _hermesDefaultModelBusy;
    private bool _hermesDefaultModelReading;
    private bool _hermesDefaultModelCatalogAuthoritative;
    private D046HermesModelCatalog? _hermesDefaultModelCatalog;
    private long _hermesDefaultModelCatalogRevision;
    private long _hermesDefaultModelDialogRevision;
    private string _hermesDefaultModelUnavailableDetail = string.Empty;
    private Task? _shutdownTask;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private AiFunctionId _selectedAiFunctionId = AiFunctionId.Invalid;

    private HermesManagementInteractionPolicy HermesManagementPolicy => new(
        _hermesCredentialReading,
        _telegramMessagingReading,
        _hermesDefaultModelReading || _providerSetupReading,
        (_hermesCredentialBusy && !_hermesCredentialReading)
            || (_telegramMessagingBusy && !_telegramMessagingReading)
            || _hermesDefaultModelBusy || _telegramVoiceEditor.IsBusy
            || (_providerSetupOperation && !_providerSetupReading)
            || LilithExpressionPanel?.IsOperationBusy == true
            || _workbenchConnectionRecovery is not null,
        _hermesCredentialCatalogAuthoritative,
        _telegramStatusAuthoritative,
        _shutdownStarted);

    private bool HermesManagementBusy => HermesManagementPolicy.AnyOperationInProgress;

    public MainWindow()
        : this(
            SourcePredeploymentDisabledNetworkBoundary.Instance,
            LilithRuntimeBootstrap.ForCurrentDistribution())
    {
    }

    internal MainWindow(ILilithRuntimeBootstrap runtimeBootstrap)
        : this(
            SourcePredeploymentDisabledNetworkBoundary.Instance,
            runtimeBootstrap)
    {
    }

    internal MainWindow(IProviderControlCenterNetworkBoundary providerNetworkBoundary)
        : this(
            providerNetworkBoundary,
            LilithRuntimeBootstrap.ForCurrentDistribution())
    {
    }

    internal MainWindow(
        IProviderControlCenterNetworkBoundary providerNetworkBoundary,
        ILilithRuntimeBootstrap runtimeBootstrap)
    {
        _providerNetworkBoundary = providerNetworkBoundary
            ?? throw new ArgumentNullException(nameof(providerNetworkBoundary));
        _runtimeBootstrap = runtimeBootstrap
            ?? throw new ArgumentNullException(nameof(runtimeBootstrap));
        _providerNetworkActionsEnabled = providerNetworkBoundary
            is not SourcePredeploymentDisabledNetworkBoundary;
        RetireLegacyProviderCredentialAuthority();
        _providerStore = UnifiedProviderControlPlaneStore.ForCurrentUser();
        _legacyDeepSeekStore = DeepSeekControlPlaneStore.ForCurrentUser();
        _aiFunctionRoutingStore = D045AiFunctionRoutingStore.ForCurrentUser();
        _workbenchModelCoordinator = new D045WorkbenchModelSelectionCoordinator(
            _providerStore,
            _aiFunctionRoutingStore);
        InitializeComponent();
        InitializeExpressionEditor();
        _workbenchModelCoordinator.SelectionChanged +=
            WorkbenchModelCoordinator_SelectionChanged;
        ApplyProviderNetworkActionAvailability();
        _pages = new Dictionary<string, Grid>(StringComparer.Ordinal)
        {
            ["Home"] = HomePage,
            ["Providers"] = ProvidersPage,
            ["Lilith"] = LilithPage,
            ["Interaction"] = InteractionPage,
            ["Settings"] = SettingsPage
        };
        _navigation = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["Home"] = HomeNav,
            ["Providers"] = ProvidersNav,
            ["Lilith"] = LilithNav,
            ["Interaction"] = InteractionNav,
            ["Settings"] = SettingsNav
        };
        _liveTelemetryTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            async (_, _) => await ObserveVisiblePageAsync(),
            Dispatcher);
        DataContext = ControlCenterReadOnlyProjection.Offline(null);
        RenderProviderControlPlane();
        SelectPage("Home");
        Loaded += async (_, _) =>
        {
            _ = PreconnectHermesWorkbenchAsync();
            await RefreshAsync();
            _liveTelemetryTimer.Start();
            RefreshProviderControlPlane();
        };
        Activated += (_, _) =>
        {
            RenderProviderControlPlane();
            _workbenchModelCoordinator.Refresh();
            RequestVisiblePageRefresh();
        };
        Closing += MainWindow_Closing;
        InitializeDesktopSettings();
    }

    private static void RetireLegacyProviderCredentialAuthority()
    {
        try
        {
            _ = LegacyProviderCredentialAuthorityRetirement
                .RetireForCurrentUser();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            // Retire() marks this profile fail-closed in-process before it
            // touches the filesystem. Hermes remains available even if an
            // old process temporarily holds one of the retired artifacts.
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted || _systemShutdown)
            return;

        if (_playerSaveBusy)
        {
            e.Cancel = true;
            return;
        }
        if (_playerSavePrepared)
        {
            e.Cancel = true;
            await CloseAfterPlayerSaveAsync();
            return;
        }

        if (!_exitRequested && HideToTray())
        {
            e.Cancel = true;
            return;
        }

        // Keep the dispatcher alive until the shared Workbench owner has
        // released voice, view-model, and pipe resources. OnMainWindowClose
        // may otherwise end the process at the first yielded await.
        e.Cancel = true;
        if (_shutdownStarted || _shutdownTask is not null)
            return;

        if (!ProtectExpressionBeforeExit()) { _exitRequested = false; return; }

        _shutdownStarted = true;
        CloseSettingsDialogsForShutdown();
        _liveTelemetryTimer.Stop();
        IsEnabled = false;
        if (_hermesWorkbenchWindow is not null)
            _hermesWorkbenchWindow.IsEnabled = false;
        HermesCapabilityCatalogOverlay.Visibility = Visibility.Collapsed;
        HermesNativeToolsOverlay.Visibility = Visibility.Collapsed;
        if (Application.Current is App && !await ControlCenterRuntimeExit.StopAsync())
        {
            _shutdownStarted = false;
            _exitRequested = false;
            IsEnabled = true;
            if (_hermesWorkbenchWindow is not null) _hermesWorkbenchWindow.IsEnabled = true;
            _liveTelemetryTimer.Start();
            ShowFromTray();
            MessageBox.Show(this, Texts["tray.exitFailed"], "Lilith", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _shutdownTask = ShutdownOwnedResourcesAsync();
        try
        {
            await _shutdownTask;
        }
        finally
        {
            DisposeDesktopIntegration();
            _shutdownCompleted = true;
            ScheduleFinalClose();
        }
    }

    private void ScheduleFinalClose()
    {
        // ShutdownOwnedResourcesAsync can complete synchronously after a
        // faulted Workbench initialization. Never re-enter Window.Close from
        // the Closing event that was just cancelled; let that event unwind
        // before asking WPF to perform the final, non-cancelled close.
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(Close));
    }

    private async Task ShutdownOwnedResourcesAsync()
    {
        _liveTelemetryTimer.Stop();
        await StopDailyNoteApprovalAsync();
        _providerLifetime.Cancel();
        _hermesFaultRecoveryDraft = null;

        // Reconnection may already have detached the old host from the main
        // field while its voice/pipe teardown is still running. Join that
        // existing task as part of ordinary bounded application shutdown.
        if (_workbenchConnectionRecovery?.Cleanup is { } connectionRetirement)
        {
            try
            {
                await connectionRetirement.WaitAsync(OwnedResourceShutdownTimeout);
            }
            catch (TimeoutException)
            {
                _ = connectionRetirement.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted
                        | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception)
            {
                // Final shutdown must not start another retirement attempt.
            }
        }

        var host = _hermesWorkbenchHost;
        _hermesWorkbenchHost = null;
        UsageBudgetSettingsPanel.Detach();
        HomeLatestUsagePanel.Detach();
        LilithMemoryManagementPanel.DetachMemoryProjection();
        LilithExpressionPanel.DetachExpressionProjection();
        if (host is not null)
        {
            try
            {
                var cleanup = host.DisposeAsync().AsTask();
                try
                {
                    await cleanup.WaitAsync(OwnedResourceShutdownTimeout);
                }
                catch (TimeoutException)
                {
                    // The host detaches UI callbacks before awaiting its
                    // best-effort transport cleanup. Do not let a broken
                    // external pipe keep the whole Control Center alive.
                    _ = cleanup.ContinueWith(
                        static task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted
                            | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch (Exception)
            {
                // Final application shutdown must continue. The host already
                // detaches callbacks before its awaited best-effort cleanup.
            }
        }

        _hermesWorkbenchWindow = null;
        _workbenchModelCoordinator.SelectionChanged -=
            WorkbenchModelCoordinator_SelectionChanged;
        if (_providerNetworkBoundary is IDisposable disposable)
            disposable.Dispose();
        _providerLifetime.Dispose();
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
            SelectPage(page);
    }

    private void SelectPage(string page) => SelectPageCore(page, refresh: true);

    private void SelectPageCore(string page, bool refresh)
    {
        _visiblePage = page;
        foreach (var entry in _pages)
        {
            entry.Value.Visibility = entry.Key == page
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        foreach (var entry in _navigation)
        {
            var selected = entry.Key == page;
            System.Windows.Controls.Primitives.Selector.SetIsSelected(entry.Value, selected);
        }
        HeaderBreadcrumb.Text = Texts.Get("nav." + page.ToLowerInvariant(), "Lilith Control Center");
        if (refresh && string.Equals(page, "Providers", StringComparison.Ordinal))
        {
            RefreshProviderControlPlane();
            _ = RefreshHermesManagementAsync();
        }
        if (refresh && string.Equals(page, "Lilith", StringComparison.Ordinal))
        {
            _ = RefreshLilithMemoryManagementAsync();
            _ = LilithExpressionPanel.RefreshExpressionAsync();
        }
        if (refresh && string.Equals(page, "Settings", StringComparison.Ordinal))
            _ = RefreshUsageBudgetAsync();
        if (!string.Equals(page, "Providers", StringComparison.Ordinal))
            HideProviderOverlays();
        if (refresh) RequestVisiblePageRefresh();
    }

    private async Task RefreshLilithMemoryManagementAsync()
    {
        if (_shutdownStarted || _workbenchConnectionRecovery is not null)
            return;

        D046HermesWorkbenchHost? host = null;
        try
        {
            host = GetOrCreateHermesWorkbenchHost();
            await LilithMemoryManagementPanel.RefreshMemoryAsync();
            if (!_shutdownStarted
                && ReferenceEquals(_hermesWorkbenchHost, host)
                && host.RequiresProjectionRecovery)
            {
                await RetireFaultedHermesWorkbenchHostAsync(host);
            }
        }
        catch (OperationCanceledException) when (
            _providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            LilithMemoryManagementPanel.DetachMemoryProjection();
            if (!_shutdownStarted
                && host is not null
                && ReferenceEquals(_hermesWorkbenchHost, host)
                && host.RequiresProjectionRecovery)
            {
                await RetireFaultedHermesWorkbenchHostAsync(host);
            }
        }
    }

    private async Task RefreshHermesManagementAsync()
    {
        await RefreshHermesCredentialStatusAsync();
        if (ProviderSetupDialogIsOpen) await RefreshProviderSetupAsync();
        if (TelegramDialogIsOpen) await RefreshTelegramMessagingAsync();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void QuickNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
            SelectPage(page);
    }

    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        SelectPage("Settings");
        AdvancedDiagnosticsPanel.IsExpanded = true;
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        SelectPage("Settings");
        AdvancedDiagnosticsPanel.IsExpanded = true;
    }

    private async Task PreconnectHermesWorkbenchAsync()
    {
        D046HermesWorkbenchHost? host = null;
        try
        {
            host = GetOrCreateHermesWorkbenchHost();
            await host.PreconnectAsync(_providerLifetime.Token);
        }
        catch (OperationCanceledException) when (
            _providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_shutdownStarted
                && host is not null
                && ReferenceEquals(_hermesWorkbenchHost, host)
                && host.RequiresProjectionRecovery)
            {
                await RetireFaultedHermesWorkbenchHostAsync(host);
            }
        }
    }

    private async void OpenWorkbench_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted)
            return;

        if (_hermesWorkbenchWindow is not { IsLoaded: true }
            && _hermesWorkbenchHost is
                { RequiresProjectionRecovery: true } cachedHost)
        {
            await RetireFaultedHermesWorkbenchHostAsync(cachedHost);
            if (_shutdownStarted)
                return;
        }

        if (_hermesWorkbenchWindow is { IsLoaded: true })
        {
            _hermesWorkbenchWindow.Show();
            if (_hermesWorkbenchWindow.WindowState == WindowState.Minimized)
                _hermesWorkbenchWindow.WindowState = WindowState.Normal;
            _hermesWorkbenchWindow.Activate();
            var loadedHost = _hermesWorkbenchHost;
            if (loadedHost?.RequiresProjectionRecovery == true
                || _workbenchConnectionRecovery is not null)
            {
                await ReconnectHermesWorkbenchWindowAsync(_hermesWorkbenchWindow);
                return;
            }
            else
            {
                return;
            }
        }

        CreateHermesWorkbenchWindow();
    }

    private void CreateHermesWorkbenchWindow()
    {
        var host = GetOrCreateHermesWorkbenchHost();
        var window = new D046HermesWorkbenchWindow(host)
        {
            Owner = this
        };
        window.Closed += async (_, _) =>
        {
            if (ReferenceEquals(_hermesWorkbenchWindow, window))
                _hermesWorkbenchWindow = null;
            var closedHost = window.BoundHost;
            if (!_shutdownStarted && closedHost.RequiresProjectionRecovery)
                await RetireFaultedHermesWorkbenchHostAsync(closedHost);
        };
        _hermesWorkbenchWindow = window;
        window.Show();
    }

    private D046HermesWorkbenchHost GetOrCreateHermesWorkbenchHost(
        bool ensureRuntimeStarted = true,
        bool connectionRecoveryOwner = false)
    {
        if (_shutdownStarted)
            throw new InvalidOperationException(
                "Hermes Workbench cannot be opened during Control Center shutdown.");
        if (_workbenchConnectionRecovery is not null && !connectionRecoveryOwner)
            throw new InvalidOperationException("Hermes connection recovery owns this transition.");
        if (_hermesWorkbenchHost is not null)
            return _hermesWorkbenchHost;

        if (ensureRuntimeStarted)
        {
            var receipt = _runtimeBootstrap.EnsureStarted();
            if (Application.Current is App app)
                _ = app.ObserveRuntimeStartupAsync(receipt);
        }
        var projection = new D046HermesWorkbenchRuntimePipeProjection(
            new D046CanonicalHermesWorkbenchRuntimeProtocol(
                D052HermesTaskPinStore.ForCurrentUser()));
        var recovery = _hermesFaultRecoveryDraft;
        var host = new D046HermesWorkbenchHost(
            projection,
            Dispatcher,
            recovery);
        if (recovery is not null)
            _hermesFaultRecoveryDraft = null;
        _hermesWorkbenchHost = host;
        AttachDailyNoteApproval(host);
        LilithMemoryManagementPanel.AttachMemoryProjection(projection);
        LilithExpressionPanel.AttachExpressionProjection(projection);
        UsageBudgetSettingsPanel.Attach(projection);
        HomeLatestUsagePanel.Attach(projection);
        HermesNativeToolsCard.DataContext = host.ViewModel;
        HermesCapabilityCatalogOverlay.DataContext = host.ViewModel;
        _nativeSettings = new NativeToolsSettingsViewModel(projection, host.ViewModel.NativeSettingsSaved, _providerLifetime.Token);
        HermesNativeToolsOverlay.DataContext = _nativeSettings;
        return host;
    }

    private async void HermesCredentialRefresh_Click(
        object sender,
        RoutedEventArgs e)
        => await RefreshHermesCredentialStatusAsync();

    private void HermesCredentialProviderList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        SelectCredentialFamily();
    }

    private void HermesCredentialApiKeyInput_PasswordChanged(
        object sender,
        RoutedEventArgs e)
        => UpdateHermesCredentialControls();

    private async void HermesCredentialSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (HermesManagementBusy
            || _shutdownStarted
            || !_hermesCredentialCatalogAuthoritative
            || SelectedCredentialProvider is not D054HermesCredentialProvider selected)
        {
            return;
        }

        if (!AdmitSettingsHostRetirement(SettingsMutationSurface.Credentials)) return;
        var apiKey = HermesCredentialApiKeyInput.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            HermesCredentialStatusText.Text = ControlCenterLocalization.Current.Get("state.enter.a.new.api.key.first.403");
            HermesCredentialApiKeyInput.Clear();
            return;
        }

        ClearCredentialMutationOutcome();
        SetHermesCredentialBusy(true);
        HermesCredentialStatusText.Text =
            $"正在儲存「{selected.DisplayName}」的 API Key…";
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            D054HermesCredentialMutationReceipt? receipt = null;
            Exception? mutationFailure = null;
            try
            {
                receipt = await host.ReplaceCredentialAsync(
                    selected.ProviderId,
                    apiKey,
                    _providerLifetime.Token);
            }
            catch (OperationCanceledException)
                when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (ArgumentException exception)
            {
                HermesCredentialStatusText.Text =
                    HermesCredentialMutationFailureText(exception, ControlCenterLocalization.Current.Get("state.save.404"));
                return;
            }
            catch (Exception exception)
            {
                mutationFailure = exception;
            }

            var oldGeneration = host.RuntimeGeneration;
            InvalidateHermesCredentialCatalog();
            Exception? reconnectFailure = null;
            try
            {
                await ReconnectHermesCredentialHostAsync(
                    host,
                    oldGeneration,
                    selected.ProviderId,
                    expectedConfigured: true,
                    _providerLifetime.Token);
            }
            catch (OperationCanceledException)
                when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                reconnectFailure = exception;
            }

            if (receipt is null)
            {
                var failure = mutationFailure
                    ?? new InvalidOperationException(
                        "Hermes credential mutation returned no result.");
                var failureText = HermesCredentialMutationFailureText(
                    failure,
                    ControlCenterLocalization.Current.Get("state.save.404"));
                if (reconnectFailure is null)
                {
                    failureText +=
                        ControlCenterLocalization.Current.Get("state.reconnected.check.the.list.above.for.the.actual.hermes.state.405");
                }
                else
                {
                    failureText +=
                        ControlCenterLocalization.Current.Get("state.lilith.has.not.finished.restarting.refresh.again.shortly.406");
                }
                HermesCredentialStatusText.Text = failureText;
                return;
            }

            if (reconnectFailure is not null)
            {
                HermesCredentialStatusText.Text =
                    $"「{receipt.DisplayName}」的 API Key 變更可能已寫入，"
                    + ControlCenterLocalization.Current.Get("state.the.switch.could.not.be.fully.confirmed.reconnect.and.check.herme.407");
                return;
            }

            var resultText = selected.Configured
                ? $"「{receipt.DisplayName}」的 API Key 已替換。"
                : $"「{receipt.DisplayName}」的 API Key 已儲存。";
            if (receipt.ManagedLegacySourcesCleared)
            {
                resultText +=
                    ControlCenterLocalization.Current.Get("state.old.api.key.sources.managed.by.hermes.were.also.cleared.408")
                    + ControlCenterLocalization.Current.Get("state.keys.supplied.by.an.external.launch.environment.must.be.removed.t.409");
            }
            HermesCredentialStatusText.Text = resultText;
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            InvalidateHermesCredentialCatalog();
            HermesCredentialStatusText.Text =
                "目前無法確認 API Key 是否已儲存。"
                + ControlCenterLocalization.Current.Get("state.refresh.first.further.changes.are.blocked.until.a.successful.read.410");
        }
        finally
        {
            apiKey = string.Empty;
            HermesCredentialApiKeyInput.Clear();
            SetHermesCredentialBusy(false);
        }
    }

    private async void HermesCredentialDelete_Click(
        object sender,
        RoutedEventArgs e)
        => await DeleteHermesCredentialAsync();

    private async Task RefreshHermesCredentialStatusAsync(bool background = false)
    {
        if (HermesManagementBusy || _shutdownStarted)
        {
            _visibleRefreshRequested = true;
            return;
        }
        if (HermesCredentialApiKeyInput.SecurePassword.Length != 0
            || background && (HermesCredentialProviderList.IsKeyboardFocusWithin
                || HermesCredentialRegionSelector.IsKeyboardFocusWithin)) return;

        if (!background) ClearCredentialMutationOutcome();
        SetHermesCredentialBusy(true, readOnly: true);
        if (!background) HermesCredentialStatusText.Text = ControlCenterLocalization.Current.Get("state.reading.api.key.status.from.hermes.412");
        D046HermesWorkbenchHost? observedHost = null;
        try
        {
            observedHost = GetOrCreateHermesWorkbenchHost();
            var catalog = await LoadHermesCredentialCatalogAsync();
            if (_shutdownStarted || !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            ShowCredentialObservationStatus(catalog.Providers.Count == 0
                ? ControlCenterLocalization.Current.Get("state.hermes.has.no.manageable.api.key.providers.413")
                : ControlCenterLocalization.Current.Get("state.api.key.status.loaded.from.hermes.414"), background);
            // Credential status is a complete independent read. Full model
            // discovery belongs to opening the model picker, not this page.
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_shutdownStarted || observedHost is not null && !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            InvalidateHermesCredentialCatalog();
            ShowCredentialObservationStatus(
                HermesCredentialFailureText(exception, ControlCenterLocalization.Current.Get("state.read.416"))
                + ControlCenterLocalization.Current.Get("state.status.unknown.reload.successfully.before.saving.or.removing.417"), background, failed: true);
        }
        finally
        {
            SetHermesCredentialBusy(false);
        }
    }

    private async Task<D054HermesCredentialCatalog> LoadHermesCredentialCatalogAsync(
        string? preferredProviderId = null,
        CancellationToken cancellationToken = default)
    {
        preferredProviderId ??= SelectedCredentialProvider?.ProviderId;
        var host = GetOrCreateHermesWorkbenchHost();
        var catalog = await host.GetCredentialStatusAsync(
                cancellationToken == default
                    ? _providerLifetime.Token
                    : cancellationToken);
        if (!ReferenceEquals(host, _hermesWorkbenchHost))
            throw new OperationCanceledException("Credential observation belongs to a retired host.");
        if (!_shutdownStarted && HermesCredentialApiKeyInput.SecurePassword.Length == 0)
            ApplyHermesCredentialCatalog(catalog, preferredProviderId);
        return catalog;
    }

    private void ApplyHermesCredentialCatalog(
        D054HermesCredentialCatalog catalog,
        string? preferredProviderId)
    {
        ApplyCredentialFamilies(catalog, preferredProviderId);
        _hermesCredentialCatalogAuthoritative = true;
        UpdateHermesCredentialControls();
    }

    private void InvalidateHermesCredentialCatalog()
    {
        _hermesCredentialCatalogAuthoritative = false;
        _applyingCredentialCatalog = true;
        try
        {
            HermesCredentialProviderList.SelectedItem = null;
            HermesCredentialProviderList.ItemsSource = Array.Empty<CredentialProviderFamily>();
            HermesCredentialRegionSelector.ItemsSource = null;
        }
        finally { _applyingCredentialCatalog = false; }
        HermesCredentialApiKeyInput.Clear();
        InvalidateHermesDefaultModelCatalog(
            string.IsNullOrEmpty(_hermesDefaultModelUnavailableDetail)
                ? ControlCenterLocalization.Current.Get("state.available.models.have.not.been.loaded.click.the.card.to.retry.418")
                : _hermesDefaultModelUnavailableDetail);
        UpdateHermesCredentialControls();
    }

    private async Task<D046HermesModelCatalog>
        LoadHermesDefaultModelCatalogAsync(
            CancellationToken cancellationToken)
    {
        if (_hermesDefaultModelReading)
            throw new InvalidOperationException("A default-model read is already in progress.");
        _hermesDefaultModelReading = true;
        _hermesDefaultModelCatalogAuthoritative = false;
        UpdateHermesManagementControls();
        try
        {
            return await LoadHermesDefaultModelCatalogCoreAsync(cancellationToken);
        }
        finally
        {
            _hermesDefaultModelReading = false;
            UpdateHermesManagementControls();
        }
    }

    private async Task<D046HermesModelCatalog>
        LoadHermesDefaultModelCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var observedHost = GetOrCreateHermesWorkbenchHost();
        var revision = _hermesDefaultModelCatalogRevision;
        var catalog = await observedHost.GetDefaultModelOptionsAsync(cancellationToken);
        if (_shutdownStarted || revision != _hermesDefaultModelCatalogRevision
            || !ReferenceEquals(observedHost, _hermesWorkbenchHost))
            throw new OperationCanceledException("Default-model observation belongs to an obsolete editor or host.");
        if (catalog is null
            || !string.IsNullOrEmpty(catalog.TaskId)
            || !string.IsNullOrEmpty(catalog.SessionId)
            || string.IsNullOrWhiteSpace(catalog.RuntimeGeneration)
            || catalog.Options is null)
        {
            throw new InvalidDataException(
                "Hermes default model catalog was malformed.");
        }

        var pairs = new HashSet<(string ProviderId, string ModelId)>();
        foreach (var option in catalog.Options)
        {
            if (option is null
                || string.IsNullOrWhiteSpace(option.ProviderId)
                || string.IsNullOrWhiteSpace(option.ProviderDisplayName)
                || string.IsNullOrWhiteSpace(option.ModelId)
                || !pairs.Add((option.ProviderId, option.ModelId)))
            {
                throw new InvalidDataException(
                    "Hermes default model catalog contained an invalid option.");
            }
        }
        if (catalog.HasExactSelection
            && !pairs.Contains((
                catalog.ExactProviderId,
                catalog.ExactModelId)))
        {
            throw new InvalidDataException(
                "Hermes default model selection was not present in its catalog.");
        }

        _hermesDefaultModelCatalog = catalog;
        _hermesDefaultModelCatalogAuthoritative = true;
        _hermesDefaultModelUnavailableDetail = string.Empty;
        RenderHermesDefaultModelCard();
        return catalog;
    }

    private void InvalidateHermesDefaultModelCatalog(string detail)
    {
        _hermesDefaultModelCatalogRevision++;
        _hermesDefaultModelCatalogAuthoritative = false;
        // Retain the last attested presentation only. No failed read may erase
        // it or turn it into a writable catalog / guess that keys were deleted.
        _hermesDefaultModelUnavailableDetail = detail;
        RenderHermesDefaultModelCard();
        if (_selectedAiFunctionId == AiFunctionId.TextConversation
            && AiFunctionAssignmentOverlay.Visibility == Visibility.Visible)
            AiFunctionSelectionBoundaryText.Text = detail + " " + Texts["model.default.readOnlySnapshot"];
        UpdateHermesManagementControls();
    }

    private void RenderHermesDefaultModelCard()
    {
        var catalog = _hermesDefaultModelCatalog;
        if (!_hermesDefaultModelCatalogAuthoritative || catalog is null)
        {
            var retainedSelection = DefaultModelCatalogPresentation.ExactSelection(catalog);
            TextFunctionAssignmentText.Text = retainedSelection is null
                ? ControlCenterLocalization.Current.Get("state.default.model.not.loaded.419")
                : Texts.Format("model.default.lastConfirmed", retainedSelection.DisplayName);
            TextFunctionAssignmentText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            TextFunctionDetailText.Text = string.IsNullOrEmpty(_hermesDefaultModelUnavailableDetail)
                ? Texts["state.load.connected.available.hermes.models.to.set.the.default.here.420"]
                : _hermesDefaultModelUnavailableDetail;
            return;
        }

        if (catalog.Options.Count == 0)
        {
            TextFunctionAssignmentText.Text = ControlCenterLocalization.Current.Get("state.no.available.model.connected.421");
            TextFunctionAssignmentText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            TextFunctionDetailText.Text =
                ControlCenterLocalization.Current.Get("state.connect.at.least.one.provider.with.an.api.key.above.422");
            return;
        }

        var selected = catalog.HasExactSelection
            ? catalog.Options.Single(option => string.Equals(
                    option.ProviderId,
                    catalog.ExactProviderId,
                    StringComparison.Ordinal)
                && string.Equals(
                    option.ModelId,
                    catalog.ExactModelId,
                    StringComparison.Ordinal))
            : null;
        TextFunctionAssignmentText.Text = selected?.DisplayName
            ?? ControlCenterLocalization.Current.Get("state.no.default.model.selected.423");
        TextFunctionAssignmentText.SetResourceReference(TextBlock.ForegroundProperty,
            selected is null ? "WarningBrush" : "GoodBrush");
        TextFunctionDetailText.Text = selected is null
            ? ControlCenterLocalization.Current.Get("state.choose.the.default.model.for.f6.f7.and.new.chats.424")
            : ControlCenterLocalization.Current.Get("state.f6.f7.and.new.chats.use.this.default.individual.chats.can.overrid.425");
    }

    private async Task ReconnectHermesCredentialHostAsync(
        D046HermesWorkbenchHost staleHost,
        Guid staleGeneration,
        string preferredProviderId,
        bool expectedConfigured,
        CancellationToken cancellationToken)
    {
        await RetireHermesWorkbenchHostAsync(staleHost);
        using var reconnect = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        reconnect.CancelAfter(HermesCredentialReconnectTimeout);
        Exception? lastFailure = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reconnect.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Hermes did not publish a new Runtime generation in time.",
                    lastFailure);
            }

            var candidate = GetOrCreateHermesWorkbenchHost();
            D054HermesCredentialCatalog? catalog = null;
            try
            {
                catalog = await candidate.GetCredentialStatusAsync(
                    reconnect.Token);
                var generation = candidate.RuntimeGeneration;
                D054HermesCredentialGenerationAdmission.RequireFresh(
                    staleGeneration,
                    generation);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
                when (reconnect.IsCancellationRequested)
            {
                await RetireHermesWorkbenchHostAsync(candidate);
                throw new TimeoutException(
                    "Hermes did not publish a new Runtime generation in time.",
                    exception);
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                await RetireHermesWorkbenchHostAsync(candidate);
            }

            if (catalog is not null)
            {
                // A fresh generation is useful even when its authoritative
                // state contradicts the mutation receipt, so keep it connected
                // and show the actual catalog. The caller will present this as
                // uncertain; only the exact postcondition admits success.
                ApplyHermesCredentialCatalog(catalog, preferredProviderId);
                D054HermesCredentialMutationPostcondition.Require(
                    catalog,
                    preferredProviderId,
                    expectedConfigured);
                // Fresh-generation credential postcondition above is the
                // completion boundary. Model discovery is unrelated to it.
                InvalidateHermesDefaultModelCatalog(
                    ControlCenterLocalization.Current["management.modelsOnDemand"]);
                return;
            }

            try
            {
                await Task.Delay(
                    HermesCredentialReconnectRetryDelay,
                    reconnect.Token);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                throw new TimeoutException(
                    "Hermes did not publish a new Runtime generation in time.",
                    lastFailure ?? exception);
            }
        }
    }

    private async Task RetireFaultedHermesWorkbenchHostAsync(
        D046HermesWorkbenchHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (OwnsConnectionRecoveryHost(host) || !EditorsAllowHostRecovery)
            return;
        if (ReferenceEquals(_hermesWorkbenchHost, host)
            && host.RequiresProjectionRecovery)
        {
            var recovery = host.ViewModel.CaptureFaultRecoveryDraft();
            if (recovery is not null)
                _hermesFaultRecoveryDraft = recovery;
        }

        await RetireHermesWorkbenchHostAsync(host);
    }

    private async Task RetireHermesWorkbenchHostAsync(
        D046HermesWorkbenchHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (OwnsConnectionRecoveryHost(host))
            return;
        await StopDailyNoteApprovalAsync(host);
        if (OwnsConnectionRecoveryHost(host))
            return;
        DetachHermesWorkbenchHostPresentation(host);

        var cleanup = host.DisposeAsync().AsTask();
        try
        {
            await cleanup.WaitAsync(HermesCredentialHostRetireTimeout);
        }
        catch (TimeoutException)
        {
            _ = cleanup.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception)
        {
            // Existing non-recovery owners retain their bounded cleanup behavior.
        }
    }

    private void DetachHermesWorkbenchHostPresentation(
        D046HermesWorkbenchHost host,
        D046HermesWorkbenchWindow? retainedWindow = null)
    {
        if (ReferenceEquals(_hermesWorkbenchHost, host))
        {
            _hermesWorkbenchHost = null;
            UsageBudgetSettingsPanel.Detach();
            ProviderSetupSettingsPanel.Model.Invalidate();
            ProviderSetupSettingsPanel.ClearSecret();
            HomeLatestUsagePanel.Detach();
            LilithMemoryManagementPanel.DetachMemoryProjection();
            LilithExpressionPanel.DetachExpressionProjection();
            InvalidateHermesDefaultModelCatalog(
                ControlCenterLocalization.Current.Get("state.hermes.reconnected.click.the.card.to.reload.its.default.model.431"));
            HermesNativeToolsCard.DataContext = null;
            HermesCapabilityCatalogOverlay.DataContext = null;
            HermesNativeToolsOverlay.DataContext = null;
            HermesCapabilityCatalogOverlay.Visibility = Visibility.Collapsed;
            HermesNativeToolsOverlay.Visibility = Visibility.Collapsed;
            InvalidateTelegramMessagingState(
                ControlCenterLocalization.Current.Get("state.hermes.reconnected.refresh.telegram.status.432"));

            var window = _hermesWorkbenchWindow;
            if (!ReferenceEquals(window, retainedWindow))
                _hermesWorkbenchWindow = null;
            if (window is { IsLoaded: true } && !ReferenceEquals(window, retainedWindow))
                window.RetireRuntimeGeneration();
        }
    }

    private void SetHermesCredentialBusy(bool busy, bool readOnly = false)
    {
        _hermesCredentialBusy = busy;
        _hermesCredentialReading = busy && readOnly;
        UpdateHermesManagementControls();
    }

    private void UpdateHermesManagementControls()
    {
        // Hold other editors still across a host-retiring mutation's awaits.
        // Their own operation controls remain authoritative inside each panel.
        var canPrepareOtherEditors = !HermesManagementPolicy.MutationOrRecoveryInProgress
            && _workbenchConnectionRecovery is null && !_shutdownStarted;
        UsageBudgetSettingsPanel.IsEnabled = canPrepareOtherEditors;
        HermesNativeToolsOverlay.IsEnabled = canPrepareOtherEditors;
        LilithMemoryManagementPanel.IsEnabled = canPrepareOtherEditors;
        LilithExpressionPanel.IsEnabled = canPrepareOtherEditors;
        UpdateProviderSetupInteractionState();
        UpdateHermesCredentialControls();
        UpdateTelegramMessagingControls();
        if (_selectedAiFunctionId == AiFunctionId.TextConversation)
        {
            AiFunctionModelSelector.IsEnabled = !HermesManagementPolicy.MutationOrRecoveryInProgress
                && !_hermesDefaultModelReading && !_shutdownStarted
                && _hermesDefaultModelCatalogAuthoritative
                && _hermesDefaultModelCatalog?.Options.Count > 0;
            AiFunctionApplyButton.IsEnabled = !HermesManagementBusy
                && HasDifferentHermesDefaultSelection();
        }
    }

    private void UpdateHermesCredentialControls()
    {
        var policy = HermesManagementPolicy;
        var selected = SelectedCredentialProvider;
        UpdateCredentialRegionPresentation(policy.CanPrepareCredential);
        // Browsing/preparing input is not a write. Keep mutation/recovery and
        // this section's own replacement read guarded; other reads can proceed.
        HermesCredentialProviderList.IsHitTestVisible = policy.CanPrepareCredential;
        HermesCredentialProviderList.IsTabStop = policy.CanPrepareCredential;
        HermesCredentialRefreshButton.IsEnabled = !policy.AnyOperationInProgress;
        HermesCredentialEmptyText.Visibility = HermesCredentialProviderList.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        HermesCredentialEmptyText.Text = ControlCenterLocalization.Current[
            _hermesCredentialReading ? "management.providersLoading"
                : _hermesCredentialCatalogAuthoritative ? "management.providersEmpty"
                    : "management.providersNotLoaded"];
        HermesCredentialSelectedProviderText.Text = selected?.DisplayName
            ?? ControlCenterLocalization.Current.Get("ui.choose.a.provider.first");
        HermesCredentialApiKeyInput.IsEnabled =
            policy.CanPrepareCredential
            && selected is not null;
        HermesCredentialSaveButton.IsEnabled =
            policy.CanMutateCredential
            && selected is not null
            && HermesCredentialApiKeyInput.SecurePassword.Length > 0;
        HermesCredentialDeleteButton.IsEnabled =
            policy.CanMutateCredential
            && selected?.Configured == true;
    }

    private static string HermesCredentialFailureText(
        Exception exception,
        string operation)
        => exception switch
        {
            ArgumentException =>
                ControlCenterLocalization.Current.Get("state.invalid.api.key.format.check.for.leading.or.trailing.spaces.433"),
            TimeoutException or IOException =>
                $"目前無法連線，API Key 沒有變更。請確認莉莉絲已啟動後再{operation}一次。",
            D054HermesCredentialOperationException =>
                $"這次{operation}未完成，API Key 沒有變更。請稍後再試。",
            InvalidDataException =>
                "暫時無法讀取狀態，API Key 沒有變更。請重新整理後再試。",
            _ =>
                $"目前無法完成{operation}，API Key 沒有變更。請稍後再試一次。"
        };

    private static string HermesCredentialMutationFailureText(
        Exception exception,
        string operation)
        => exception switch
        {
            ArgumentException =>
                ControlCenterLocalization.Current.Get("state.invalid.api.key.format.check.for.leading.or.trailing.spaces.433"),
            D054HermesCredentialOperationException =>
                ControlCenterLocalization.Current.Format("credential.mutation.notCompleted", operation),
            _ =>
                ControlCenterLocalization.Current.Format("credential.mutation.outcomeUnknown", operation)
        };

    private async void TelegramRefresh_Click(
        object sender,
        RoutedEventArgs e)
        => await RefreshTelegramMessagingAsync();

    private void TelegramBotTokenInput_PasswordChanged(
        object sender,
        RoutedEventArgs e)
        => UpdateTelegramMessagingControls();

    private async void TelegramSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (HermesManagementBusy
            || _shutdownStarted
            || !_telegramStatusAuthoritative)
        {
            return;
        }

        if (!AdmitSettingsHostRetirement(SettingsMutationSurface.TelegramToken)) return;
        var botToken = TelegramBotTokenInput.Password;
        if (string.IsNullOrWhiteSpace(botToken))
        {
            TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.enter.your.telegram.bot.token.first.434");
            TelegramBotTokenInput.Clear();
            return;
        }

        SetTelegramMessagingBusy(true);
        TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.connecting.telegram.through.hermes.435");
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            var oldGeneration = host.RuntimeGeneration;
            HermesTelegramTokenMutationReceipt? receipt = null;
            Exception? mutationFailure = null;
            try
            {
                receipt = await host.ReplaceTelegramTokenAsync(
                    botToken,
                    _providerLifetime.Token);
            }
            catch (OperationCanceledException)
                when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (ArgumentException exception)
            {
                TelegramStatusText.Text = TelegramMutationFailureText(
                    exception,
                    ControlCenterLocalization.Current.Get("state.save.404"));
                return;
            }
            catch (Exception exception)
            {
                mutationFailure = exception;
            }
            finally
            {
                botToken = string.Empty;
                TelegramBotTokenInput.Clear();
            }

            InvalidateTelegramMessagingState(
                ControlCenterLocalization.Current.Get("state.reloading.the.telegram.connection.436"));
            Exception? reconnectFailure = null;
            try
            {
                await ReconnectTelegramMessagingHostAsync(
                    host,
                    oldGeneration,
                    expectedConfigured: true,
                    _providerLifetime.Token);
            }
            catch (OperationCanceledException)
                when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                reconnectFailure = exception;
            }

            if (receipt is null)
            {
                TelegramStatusText.Text = TelegramMutationFailureText(
                    mutationFailure
                        ?? new InvalidOperationException(
                            "Telegram mutation returned no result."),
                    ControlCenterLocalization.Current.Get("state.save.404"));
                if (reconnectFailure is null)
                {
                    TelegramStatusText.Text +=
                        ControlCenterLocalization.Current.Get("state.reconnected.check.the.status.above.for.the.result.437");
                }
                else
                {
                    TelegramStatusText.Text +=
                        ControlCenterLocalization.Current.Get("state.lilith.is.not.ready.yet.refresh.again.shortly.438");
                }
                return;
            }

            if (reconnectFailure is not null)
            {
                TelegramStatusText.Text =
                    ControlCenterLocalization.Current.Get("state.the.bot.token.may.be.saved.but.the.connection.is.not.confirmed.439")
                    + ControlCenterLocalization.Current.Get("state.refresh.again.shortly.440");
                return;
            }

            TelegramStatusText.Text = _telegramStatus?.State switch
            {
                HermesTelegramConnectionState.Connected =>
                    ControlCenterLocalization.Current.Get("state.telegram.connected.message.your.bot.then.approve.your.account.on.441"),
                HermesTelegramConnectionState.Attention =>
                    "Bot Token 已儲存，但 Telegram 需要處理；請確認 Token 後再試一次。",
                _ => ControlCenterLocalization.Current.Get("state.bot.token.saved.telegram.is.connecting.442")
            };
        }
        catch (OperationCanceledException)
            when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            InvalidateTelegramMessagingState(
                ControlCenterLocalization.Current.Get("state.save.result.unconfirmed.refresh.before.making.another.change.443"));
        }
        finally
        {
            botToken = string.Empty;
            TelegramBotTokenInput.Clear();
            SetTelegramMessagingBusy(false);
        }
    }

    private async void TelegramRemove_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (HermesManagementBusy
            || _shutdownStarted
            || !_telegramStatusAuthoritative
            || _telegramStatus?.Configured != true)
        {
            return;
        }

        if (!AdmitSettingsHostRetirement(SettingsMutationSurface.TelegramToken)) return;
        var confirmation = MessageBox.Show(
            this,
            "確定移除 Telegram 連接？\n\n"
                + "這會刪除已儲存的 Bot Token，並停止 Telegram 收發；"
                + ControlCenterLocalization.Current.Get("state.f6.f7.control.center.and.existing.conversation.memories.will.not.444"),
            ControlCenterLocalization.Current.Get("state.remove.telegram.connection.445"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            TelegramBotTokenInput.Clear();
            return;
        }

        if (HermesManagementBusy || _shutdownStarted
            || !AdmitSettingsHostRetirement(SettingsMutationSurface.TelegramToken)) return;
        SetTelegramMessagingBusy(true);
        TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.removing.telegram.connection.446");
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            var oldGeneration = host.RuntimeGeneration;
            HermesTelegramTokenMutationReceipt? receipt = null;
            Exception? mutationFailure = null;
            try
            {
                receipt = await host.RemoveTelegramTokenAsync(
                    _providerLifetime.Token);
            }
            catch (OperationCanceledException)
                when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                mutationFailure = exception;
            }

            InvalidateTelegramMessagingState(
                ControlCenterLocalization.Current.Get("state.reloading.the.telegram.connection.436"));
            Exception? reconnectFailure = null;
            try
            {
                await ReconnectTelegramMessagingHostAsync(
                    host,
                    oldGeneration,
                    expectedConfigured: false,
                    _providerLifetime.Token);
            }
            catch (OperationCanceledException)
                when (_providerLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                reconnectFailure = exception;
            }

            if (receipt is null)
            {
                TelegramStatusText.Text = TelegramMutationFailureText(
                    mutationFailure
                        ?? new InvalidOperationException(
                            "Telegram mutation returned no result."),
                    ControlCenterLocalization.Current.Get("state.remove.447"));
                if (reconnectFailure is null)
                {
                    TelegramStatusText.Text +=
                        ControlCenterLocalization.Current.Get("state.reconnected.check.the.status.above.for.the.result.437");
                }
                else
                {
                    TelegramStatusText.Text +=
                        ControlCenterLocalization.Current.Get("state.lilith.is.not.ready.yet.refresh.again.shortly.438");
                }
                return;
            }

            if (reconnectFailure is not null)
            {
                TelegramStatusText.Text =
                    ControlCenterLocalization.Current.Get("state.telegram.may.be.disconnected.but.completion.is.not.confirmed.448")
                    + ControlCenterLocalization.Current.Get("state.refresh.again.shortly.440");
                return;
            }
            TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.telegram.connection.removed.conversation.memories.were.kept.449");
        }
        catch (OperationCanceledException)
            when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            InvalidateTelegramMessagingState(
                ControlCenterLocalization.Current.Get("state.removal.result.unconfirmed.refresh.before.making.another.change.450"));
        }
        finally
        {
            TelegramBotTokenInput.Clear();
            SetTelegramMessagingBusy(false);
        }
    }

    private async void TelegramApprovePairing_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (HermesManagementBusy
            || _shutdownStarted
            || !_telegramStatusAuthoritative
            || sender is not Button
            {
                Tag: HermesTelegramPendingPairing pending
            })
        {
            return;
        }

        SetTelegramMessagingBusy(true);
        TelegramStatusText.Text = $"正在核准「{pending.DisplayName}」…";
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            _ = await host.ApproveTelegramPairingAsync(
                pending.RequestId,
                _providerLifetime.Token);
            await RefreshTelegramPairingCoreAsync(
                host,
                _providerLifetime.Token);
            TelegramStatusText.Text =
                $"「{pending.DisplayName}」已可在 Telegram 與莉莉絲對話。";
        }
        catch (OperationCanceledException)
            when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TelegramStatusText.Text = TelegramOperationFailureText(
                exception,
                ControlCenterLocalization.Current.Get("state.approve.account.451"));
        }
        finally
        {
            TelegramBotTokenInput.Clear();
            SetTelegramMessagingBusy(false);
        }
    }

    private async void TelegramRevokeUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (HermesManagementBusy
            || _shutdownStarted
            || !_telegramStatusAuthoritative
            || sender is not Button { Tag: HermesTelegramApprovedUser user })
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"確定撤銷 Telegram 帳號 {user.UserId}？",
            ControlCenterLocalization.Current.Get("state.revoke.telegram.account.452"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SetTelegramMessagingBusy(true);
        TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.revoking.telegram.account.453");
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            _ = await host.RevokeTelegramUserAsync(
                user.UserId,
                _providerLifetime.Token);
            await RefreshTelegramPairingCoreAsync(
                host,
                _providerLifetime.Token);
            TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.telegram.account.approval.revoked.454");
        }
        catch (OperationCanceledException)
            when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TelegramStatusText.Text = TelegramOperationFailureText(
                exception,
                ControlCenterLocalization.Current.Get("state.revoke.account.455"));
        }
        finally
        {
            TelegramBotTokenInput.Clear();
            SetTelegramMessagingBusy(false);
        }
    }

    private async Task RefreshTelegramMessagingAsync(bool background = false)
    {
        if (HermesManagementBusy || _shutdownStarted)
        {
            _visibleRefreshRequested = true;
            return;
        }
        if (TelegramBotTokenInput.SecurePassword.Length != 0 || _telegramVoiceEditor.CanSave) return;

        SetTelegramMessagingBusy(true, readOnly: true);
        if (!background) TelegramStatusText.Text = ControlCenterLocalization.Current.Get("state.reading.telegram.connection.status.456");
        D046HermesWorkbenchHost? observedHost = null;
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            observedHost = host;
            var status = await host.GetTelegramStatusAsync(
                _providerLifetime.Token);
            if (_shutdownStarted || !ReferenceEquals(host, _hermesWorkbenchHost)) return;
            ApplyTelegramStatus(status);
            if (status.Configured)
            {
                try
                {
                    await RefreshTelegramPairingCoreAsync(
                        host,
                        _providerLifetime.Token);
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException)
                {
                    if (_shutdownStarted || !ReferenceEquals(host, _hermesWorkbenchHost)) return;
                    InvalidateTelegramPairingCatalog();
                    TelegramStatusText.Text =
                        TelegramStatusSummary(status)
                        + ControlCenterLocalization.Current.Get("state.account.approvals.are.temporarily.unavailable.457");
                    return;
                }
            }
            else
            {
                ApplyTelegramPairingCatalog(
                    new HermesTelegramPairingCatalog(
                        Array.Empty<HermesTelegramPendingPairing>(),
                        Array.Empty<HermesTelegramApprovedUser>()));
            }
            TelegramStatusText.Text = TelegramStatusSummary(status);
        }
        catch (OperationCanceledException)
            when (_providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_shutdownStarted || observedHost is not null && !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            InvalidateTelegramMessagingState(
                TelegramOperationFailureText(exception, ControlCenterLocalization.Current.Get("state.read.telegram.status.458")));
        }
        finally
        {
            SetTelegramMessagingBusy(false);
        }
    }

    private async Task RefreshTelegramPairingCoreAsync(
        D046HermesWorkbenchHost host,
        CancellationToken cancellationToken)
    {
        var catalog = await host.GetTelegramPairingAsync(cancellationToken);
        if (_shutdownStarted || !ReferenceEquals(host, _hermesWorkbenchHost)) return;
        ApplyTelegramPairingCatalog(catalog);
    }

    private async Task ReconnectTelegramMessagingHostAsync(
        D046HermesWorkbenchHost staleHost,
        Guid staleGeneration,
        bool expectedConfigured,
        CancellationToken cancellationToken)
    {
        await RetireHermesWorkbenchHostAsync(staleHost);
        using var reconnect = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        reconnect.CancelAfter(HermesCredentialReconnectTimeout);
        Exception? lastFailure = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reconnect.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Hermes did not publish a new Runtime generation in time.",
                    lastFailure);
            }

            var candidate = GetOrCreateHermesWorkbenchHost();
            HermesTelegramStatus? status = null;
            try
            {
                status = await candidate.GetTelegramStatusAsync(reconnect.Token);
                HermesTelegramGenerationAdmission.RequireFresh(
                    staleGeneration,
                    candidate.RuntimeGeneration);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
                when (reconnect.IsCancellationRequested)
            {
                await RetireHermesWorkbenchHostAsync(candidate);
                throw new TimeoutException(
                    "Hermes did not publish a new Runtime generation in time.",
                    exception);
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                await RetireHermesWorkbenchHostAsync(candidate);
            }

            if (status is not null)
            {
                ApplyTelegramStatus(status);
                if (status.Configured != expectedConfigured)
                {
                    throw new InvalidDataException(
                        "The fresh Runtime contradicted the Telegram mutation.");
                }
                if (status.Configured)
                {
                    try
                    {
                        await RefreshTelegramPairingCoreAsync(
                            candidate,
                            reconnect.Token);
                    }
                    catch (Exception exception)
                        when (exception is not OperationCanceledException)
                    {
                        InvalidateTelegramPairingCatalog();
                    }
                }
                else
                {
                    ApplyTelegramPairingCatalog(
                        new HermesTelegramPairingCatalog(
                            Array.Empty<HermesTelegramPendingPairing>(),
                            Array.Empty<HermesTelegramApprovedUser>()));
                }
                return;
            }

            try
            {
                await Task.Delay(
                    HermesCredentialReconnectRetryDelay,
                    reconnect.Token);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                throw new TimeoutException(
                    "Hermes did not publish a new Runtime generation in time.",
                    lastFailure ?? exception);
            }
        }
    }

    private void ApplyTelegramStatus(HermesTelegramStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _telegramStatus = status;
        _telegramStatusAuthoritative = true;
        TelegramConnectionStateText.Text = status.State switch
        {
            HermesTelegramConnectionState.Unconfigured => ControlCenterLocalization.Current.Get("ui.not.configured"),
            HermesTelegramConnectionState.Starting => ControlCenterLocalization.Current.Get("state.connecting.459"),
            HermesTelegramConnectionState.Connected => ControlCenterLocalization.Current.Get("state.connected.460"),
            HermesTelegramConnectionState.Attention => ControlCenterLocalization.Current.Get("state.needs.attention.461"),
            _ => ControlCenterLocalization.Current.Get("state.status.unknown.23")
        };
        TelegramConnectionStateText.SetResourceReference(TextBlock.ForegroundProperty, status.State switch
        {
            HermesTelegramConnectionState.Connected => "GoodBrush",
            HermesTelegramConnectionState.Starting
                or HermesTelegramConnectionState.Attention => "WarningBrush",
            _ => "MutedBrush"
        });
        UpdateTelegramMessagingControls();
    }

    private void ApplyTelegramPairingCatalog(
        HermesTelegramPairingCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        TelegramPendingPairingList.ItemsSource = catalog.Pending;
        TelegramApprovedUserList.ItemsSource = catalog.Approved;
        TelegramPendingEmptyText.Visibility = catalog.Pending.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        TelegramApprovedEmptyText.Visibility = catalog.Approved.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_telegramVoiceEditor.UserId is { } selectedVoiceUser
            && !catalog.Approved.Any(user => user.UserId == selectedVoiceUser))
            InvalidateTelegramVoiceEditor(clearSelection: true);
    }

    private void InvalidateTelegramPairingCatalog()
    {
        InvalidateTelegramVoiceEditor(clearSelection: true);
        TelegramPendingPairingList.ItemsSource =
            Array.Empty<HermesTelegramPendingPairing>();
        TelegramApprovedUserList.ItemsSource =
            Array.Empty<HermesTelegramApprovedUser>();
        TelegramPendingEmptyText.Visibility = Visibility.Visible;
        TelegramApprovedEmptyText.Visibility = Visibility.Visible;
    }

    private void InvalidateTelegramMessagingState(string message)
    {
        _telegramStatus = null;
        _telegramStatusAuthoritative = false;
        TelegramConnectionStateText.Text = ControlCenterLocalization.Current.Get("ui.not.loaded.yet");
        TelegramConnectionStateText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        InvalidateTelegramPairingCatalog();
        TelegramBotTokenInput.Clear();
        TelegramStatusText.Text = message;
        UpdateTelegramMessagingControls();
    }

    private void SetTelegramMessagingBusy(bool busy, bool readOnly = false)
    {
        _telegramMessagingBusy = busy;
        _telegramMessagingReading = busy && readOnly;
        UpdateHermesManagementControls();
    }

    private void UpdateTelegramMessagingControls()
    {
        var policy = HermesManagementPolicy;
        var mutable = policy.CanMutateTelegram;
        // Guard the token editor while a status read is replacing its authority.
        // Passive observation never clears a prepared token.
        TelegramBotTokenInput.IsEnabled = mutable;
        TelegramSaveButton.IsEnabled = mutable
            && TelegramBotTokenInput.SecurePassword.Length > 0;
        TelegramRemoveButton.IsEnabled = mutable
            && _telegramStatus?.Configured == true;
        TelegramRefreshButton.IsEnabled = !HermesManagementBusy;
        TelegramPendingPairingList.IsHitTestVisible = !HermesManagementBusy;
        TelegramApprovedUserList.IsHitTestVisible = !HermesManagementBusy;
        TelegramPendingPairingList.IsEnabled = !HermesManagementBusy;
        TelegramApprovedUserList.IsEnabled = !HermesManagementBusy;
        UpdateTelegramVoiceControls();
    }

    private static string TelegramStatusSummary(HermesTelegramStatus status)
        => status.State switch
        {
            HermesTelegramConnectionState.Unconfigured =>
                "Telegram 尚未設定。建立 Bot 後，把 Bot Token 貼到左側即可連接。",
            HermesTelegramConnectionState.Starting =>
                ControlCenterLocalization.Current.Get("state.telegram.is.connecting.refresh.shortly.to.check.the.result.462"),
            HermesTelegramConnectionState.Connected =>
                ControlCenterLocalization.Current.Get("state.telegram.connected.only.approved.accounts.on.the.right.can.chat.w.463"),
            HermesTelegramConnectionState.Attention =>
                "Telegram 需要注意；請確認 Bot Token，或重新整理後再試一次。",
            _ => ControlCenterLocalization.Current.Get("state.telegram.status.unknown.464")
        };

    private static string TelegramOperationFailureText(
        Exception exception,
        string operation)
        => exception switch
        {
            TimeoutException or IOException =>
                $"目前無法連線，無法{operation}；請確認莉莉絲已啟動。",
            InvalidDataException =>
                $"暫時無法讀取 Telegram 狀態，無法{operation}。",
            HermesTelegramOperationException =>
                $"{operation}未完成；原本的 Telegram 設定已保留。",
            _ => $"目前無法{operation}；請稍後再試一次。"
        };

    private static string TelegramMutationFailureText(
        Exception exception,
        string operation)
        => exception switch
        {
            ArgumentException =>
                "這組 Bot Token 的格式不能使用；請確認前後沒有空白後再試一次。",
            HermesTelegramOperationException =>
                $"這次{operation}未完成；原本的 Telegram 設定已保留。",
            _ =>
                $"目前無法確認這次{operation}的結果；請重新整理查看實際狀態。"
        };

    internal async Task OpenHermesNativeToolsSettingsAsync()
    {
        if (_shutdownStarted)
            return;

        SelectPage("Providers");
        HideProviderOverlays();
        HermesNativeToolsOverlay.Visibility = Visibility.Visible;
        try
        {
            await RefreshHermesNativeToolsSettingsAsync();
            if (!_shutdownStarted)
                HermesNativeToolsOverlay.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (
            _providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowHermesNativeToolsBootstrapFailure();
        }
    }

    private async Task RefreshHermesNativeToolsSettingsAsync()
    {
        var host = GetOrCreateHermesWorkbenchHost();
        if (host.RequiresProjectionRecovery)
        {
            await RetireFaultedHermesWorkbenchHostAsync(host);
            if (_shutdownStarted)
                return;
            host = GetOrCreateHermesWorkbenchHost();
        }

        try
        {
            await host.PreconnectAsync(_providerLifetime.Token);
        }
        catch (ObjectDisposedException) when (!_shutdownStarted)
        {
            // Startup preconnect can fault and retire the cached host while a
            // player opens this card. Join that retirement, then bind the card
            // to one fresh host instead of letting an async UI event terminate
            // the Control Center.
            await RetireHermesWorkbenchHostAsync(host);
            if (_shutdownStarted)
                return;
            host = GetOrCreateHermesWorkbenchHost();
            await host.PreconnectAsync(_providerLifetime.Token);
        }
        catch (Exception)
        {
            if (!_shutdownStarted
                && ReferenceEquals(_hermesWorkbenchHost, host)
                && host.RequiresProjectionRecovery)
            {
                try
                {
                    await RetireFaultedHermesWorkbenchHostAsync(host);
                }
                catch (Exception)
                {
                    // The visible failure state remains retryable even if the
                    // broken transport cannot finish best-effort cleanup.
                }
            }
            throw;
        }

        if (_shutdownStarted)
            return;
        if (_nativeSettings is not null) await _nativeSettings.RefreshAsync();
    }

    private void ShowHermesNativeToolsBootstrapFailure()
    {
        HermesNativeToolsOverlay.Visibility = Visibility.Visible;
        ControlCenterHermesNativeToolsStatus.SetCurrentValue(
            TextBlock.TextProperty,
            "目前無法連線；工具設定沒有變更。請稍後重新整理。");
        ControlCenterHermesNativeToolsRefreshButton.SetCurrentValue(
            IsEnabledProperty,
            true);
    }

    private async void OpenHermesNativeToolsSettings_Click(
        object sender,
        RoutedEventArgs e)
        => await OpenHermesNativeToolsSettingsAsync();

    private async void OpenHermesCapabilityCatalog_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_shutdownStarted)
            return;

        SelectPage("Providers");
        HideProviderOverlays();
        HermesCapabilityCatalogOverlay.Visibility = Visibility.Visible;
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            await host.ViewModel.EnsureCapabilityCatalogLoadedAsync();
        }
        catch (Exception)
        {
            ShowCapabilityCatalogBootstrapFailure();
        }
    }

    private void CloseHermesCapabilityCatalog_Click(
        object sender,
        RoutedEventArgs e)
        => HermesCapabilityCatalogOverlay.Visibility = Visibility.Collapsed;

    private async void HermesCapabilityCatalogRefresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_shutdownStarted)
            return;

        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            await host.ViewModel.RefreshCapabilityCatalogAsync();
        }
        catch (Exception)
        {
            ShowCapabilityCatalogBootstrapFailure();
        }
    }

    private void ShowCapabilityCatalogBootstrapFailure()
    {
        HermesCapabilityCatalogStatus.SetCurrentValue(
            TextBlock.TextProperty,
            "目前無法連線，請稍後重新整理能力目錄。");
        HermesCapabilityCatalogRefreshButton.SetCurrentValue(
            IsEnabledProperty,
            true);
    }

    private void CloseHermesNativeTools_Click(
        object sender,
        RoutedEventArgs e)
    {
        _hermesWorkbenchHost?.ViewModel.CancelNativeToolsChanges();
        HermesNativeToolsOverlay.Visibility = Visibility.Collapsed;
    }

    private async void HermesNativeToolsRefresh_Click(
        object sender,
        RoutedEventArgs e)
        => await OpenHermesNativeToolsSettingsAsync();

    private async void HermesNativeToolsApply_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_shutdownStarted)
            return;

        if (_nativeSettings is not null)
            await _nativeSettings.SaveAsync();
    }

    private void HermesNativeToolsCancel_Click(
        object sender,
        RoutedEventArgs e)
        => _nativeSettings?.Cancel();

    private async void HermesNativeSetupApply_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted || _hermesWorkbenchHost is null
            || sender is not Button { Tag: D046HermesNativeToolsetViewModel row }) return;
        if (_nativeSettings is not null) await _nativeSettings.SaveProviderAsync(row);
    }

    private async void HermesNativeMcpSetupApply_Click(object sender, RoutedEventArgs e)
    {
        if (_hermesWorkbenchHost is null
            || sender is not Button { Tag: D046HermesNativeMcpViewModel row }) return;
        if (_nativeSettings is not null) await _nativeSettings.SaveMcpAsync(row);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        RefreshProviderControlPlane();
        _visibleRefreshManual = true;
        RequestVisiblePageRefresh();
    }

    private async Task RecoverFaultedHermesWorkbenchAsync()
    {
        if (_hermesWorkbenchHost is not { RequiresProjectionRecovery: true }) return;
        if (_hermesWorkbenchWindow is { IsLoaded: true } existingWindow)
        {
            await ReconnectHermesWorkbenchWindowAsync(existingWindow);
            return;
        }
        var host = _hermesWorkbenchHost;
        if (host is null || !host.RequiresProjectionRecovery)
            return;

        var reopen = _hermesWorkbenchWindow is { IsLoaded: true };
        await RetireFaultedHermesWorkbenchHostAsync(host);
        if (reopen && !_shutdownStarted)
            CreateHermesWorkbenchWindow();
    }

    private void ApplyProviderNetworkActionAvailability()
    {
        ProviderApiKeyInput.IsEnabled = _providerNetworkActionsEnabled;
        ProviderPrepareValidationButton.IsEnabled = _providerNetworkActionsEnabled;
    }

    private void RefreshProviderControlPlane()
        => RenderProviderControlPlane();

    private void RenderProviderControlPlane()
    {
        var now = DateTimeOffset.UtcNow;
        var loaded = _providerStore.Load();
        var snapshot = loaded.Accepted ? loaded.Snapshot : null;
        D045AiFunctionRoutingSnapshot? functionRouting = null;
        if (snapshot != null)
        {
            var migration = _aiFunctionRoutingStore
                .EnsureLegacyTextAssignment(snapshot);
            functionRouting = migration.Accepted
                ? migration.Snapshot
                : _aiFunctionRoutingStore.Load().Snapshot;
        }
        var activeIds = new HashSet<string>(
            snapshot?.Instances.Select(value => value.InstanceId)
                ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        foreach (var stale in _providerHealthByInstance.Keys
                     .Where(value => !activeIds.Contains(value))
                     .ToArray())
        {
            _providerHealthByInstance.Remove(stale);
        }
        var renderedHealthByInstance = new Dictionary<string, ProviderConnectionHealth>(
            StringComparer.Ordinal);
        if (snapshot != null)
        {
            foreach (var instance in snapshot.Instances)
            {
                if (_providerHealthByInstance.TryGetValue(
                        instance.InstanceId,
                        out var cached)
                    && cached.IsCurrentFor(instance, now))
                {
                    renderedHealthByInstance[instance.InstanceId] = cached.Health;
                }
                else
                {
                    _providerHealthByInstance.Remove(instance.InstanceId);
                    renderedHealthByInstance[instance.InstanceId] =
                        ProviderConnectionHealth.Unknown;
                }
            }
        }

        _aiFunctionsProjection = AiFunctionsPageProjection.BuildControlPlane(
            new AiFunctionsControlPlaneInput(
                snapshot,
                renderedHealthByInstance,
                ReadLegacyDeepSeek(),
                functionRouting),
            now);

        CloudProviderSetupList.ItemsSource =
            _aiFunctionsProjection.SetupOptions.Where(value =>
                value.UiGroup == AiProviderUiGroup.Cloud);
        LocalProviderSetupList.ItemsSource =
            _aiFunctionsProjection.SetupOptions.Where(value =>
                value.UiGroup == AiProviderUiGroup.LocalAndSelfHosted);
        RenderHermesDefaultModelCard();
    }

    private void WorkbenchModelCoordinator_SelectionChanged(
        object? sender,
        D045WorkbenchModelSelectionChangedEventArgs e)
    {
        if (e.Source != D045WorkbenchModelSelectionSource.Workbench)
            return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RenderProviderControlPlane));
            return;
        }
        RenderProviderControlPlane();
    }

    private AiFunctionsDeepSeekInput? ReadLegacyDeepSeek()
    {
        var result = _legacyDeepSeekStore.Load();
        var source = result.Accepted ? result.Snapshot : null;
        return source == null
            ? null
            : new AiFunctionsDeepSeekInput(
                source.ConfigurationGeneration,
                source.SelectedModel,
                source.CatalogModels,
                source.CatalogObservedAt,
                source.CatalogExpiresAt,
                source.CredentialConfigured);
    }

    private async void AiFunctionCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string functionName })
            return;
        if (!Enum.TryParse(functionName, out AiFunctionId functionId))
            return;
        if (functionId == AiFunctionId.TextConversation)
        {
            await OpenHermesDefaultModelAssignmentAsync();
            return;
        }
        var function = _aiFunctionsProjection.Functions.Single(value =>
            value.FunctionId == functionId);
        _selectedAiFunctionId = functionId;
        AiFunctionDialogTitleText.Text = function.DisplayName;
        AiFunctionDialogDetailText.Text = function.Detail;
        if (functionId == AiFunctionId.HigherReasoning)
        {
            AiFunctionDialogDetailText.Text +=
                "\n清單只接受已接入 Agent 工具協定的模型；「未由模型目錄驗證」表示執行層已接線，但官方目錄沒有提供該模型的逐模型工具證據。";
        }
        AiFunctionModelSelector.ItemsSource =
            function.CompatibleConfiguredModelChoices;
        AiFunctionModelSelector.SelectedItem =
            function.CompatibleConfiguredModelChoices.SingleOrDefault(value =>
                string.Equals(
                    value.DisplayText,
                    function.Assignment,
                    StringComparison.Ordinal))
            ?? function.CompatibleConfiguredModelChoices.FirstOrDefault();
        var imageChoices = _aiFunctionsProjection
            .HigherReasoningImageGenerationChoices;
        var imageRoute = _aiFunctionRoutingStore.Load().Snapshot?.Find(
            D045AiFunctionKind.HigherReasoningImageGeneration);
        AiFunctionImageModelPanel.Visibility = functionId
            == AiFunctionId.HigherReasoning
                ? Visibility.Visible
                : Visibility.Collapsed;
        AiFunctionImageModelSelector.ItemsSource = imageChoices;
        AiFunctionImageModelSelector.SelectedItem = imageChoices
            .SingleOrDefault(value => imageRoute != null
                && string.Equals(
                    value.InstanceId,
                    imageRoute.InstanceId,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.ModelId,
                    imageRoute.ModelId,
                    StringComparison.Ordinal)
                && value.ConfigurationGeneration
                    == imageRoute.ConfigurationGeneration)
            ?? imageChoices.FirstOrDefault();
        AiFunctionImageModelSelector.IsEnabled = imageChoices.Count > 0;
        AiFunctionModelSelector.IsEnabled =
            function.CanChooseConfiguredModel;
        AiFunctionApplyButton.IsEnabled = function.CanChooseConfiguredModel
            && (functionId != AiFunctionId.HigherReasoning
                || imageChoices.Count > 0);
        AiFunctionApplyButton.Visibility =
            AiFunctionApplyButton.IsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        AiFunctionOpenProviderSetupButton.Visibility =
            AiFunctionApplyButton.IsEnabled
                || function.State == AiFunctionAssignmentState.LocalService
                ? Visibility.Collapsed
                : Visibility.Visible;
        AiFunctionSelectionBoundaryText.Text =
            AiFunctionApplyButton.IsEnabled
                ? "只更新精確 instance／model／configuration generation；不會自動啟用遊戲端 Provider。"
                : function.State == AiFunctionAssignmentState.LocalService
                    ? "使用本機語音，不需要 API Key。"
                : functionId == AiFunctionId.HigherReasoning
                    ? "需要各一個可處理圖片的上位思考模型，以及明確宣告圖片輸出的生成模型。"
                    : "沒有已完成安全設定且可由目前契約證明相容的模型。";
        HideProviderOverlays();
        AiFunctionAssignmentOverlay.Visibility = Visibility.Visible;
    }

    private async void AiFunctionApplyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedAiFunctionId == AiFunctionId.TextConversation)
        {
            await ApplyHermesDefaultModelAssignmentAsync();
            return;
        }
        if (AiFunctionModelSelector.SelectedItem
            is not AiConfiguredModelChoiceVm selected)
        {
            return;
        }
        if (string.Equals(
                selected.InstanceId,
                "legacy-deepseek-v1",
                StringComparison.Ordinal))
        {
            AiFunctionSelectionBoundaryText.Text =
                "Legacy DeepSeek 只作唯讀相容；請新增 D-033 平台實例後再由新控制面選型。";
            return;
        }
        if (!TryMapFunctionKind(_selectedAiFunctionId, out var functionKind))
            return;
        var loaded = _providerStore.Load();
        if (!loaded.Accepted || loaded.Snapshot == null)
        {
            AiFunctionSelectionBoundaryText.Text = "平台設定目前無法讀取。";
            return;
        }
        if (_selectedAiFunctionId is AiFunctionId.HigherReasoning
                or AiFunctionId.ComputerActionPlanning)
        {
            var selectedInstance = loaded.Snapshot.Instances.SingleOrDefault(
                value => string.Equals(
                    value.InstanceId,
                    selected.InstanceId,
                    StringComparison.Ordinal));
            if (selectedInstance == null
                || !D045WorkbenchModelCatalogRules
                    .IsSelectableHigherReasoningModel(
                        selectedInstance,
                        selected.ModelId,
                        DateTimeOffset.UtcNow))
            {
                AiFunctionSelectionBoundaryText.Text =
                    "這個模型沒有可用的 Agent 工具協定，無法指派給上位思考。";
                return;
            }
        }
        if (_selectedAiFunctionId is AiFunctionId.HigherReasoning
                or AiFunctionId.ComputerActionPlanning)
        {
            D045AiFunctionRouteSelection? imageSelection = null;
            if (_selectedAiFunctionId == AiFunctionId.HigherReasoning)
            {
                if (AiFunctionImageModelSelector.SelectedItem
                    is not AiConfiguredModelChoiceVm imageSelected)
                {
                    AiFunctionSelectionBoundaryText.Text =
                        "請選擇圖片生成模型。";
                    return;
                }
                imageSelection = new D045AiFunctionRouteSelection(
                    D045AiFunctionKind.HigherReasoningImageGeneration,
                    imageSelected.InstanceId,
                    imageSelected.ModelId,
                    imageSelected.ConfigurationGeneration);
            }

            var synchronized =
                _workbenchModelCoordinator.SelectGlobalWorkbenchModel(
                    selected.InstanceId,
                    selected.ModelId,
                    selected.ConfigurationGeneration,
                    D045WorkbenchModelSelectionSource.MainWindow,
                    imageSelection);
            RenderProviderControlPlane();
            AiFunctionSelectionBoundaryText.Text = synchronized.Accepted
                ? "此功能的模型指派已儲存。"
                : "模型指派未更新：" + synchronized.Admission;
            if (synchronized.Accepted && synchronized.Snapshot != null)
                AiFunctionAssignmentOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var selections = new List<D045AiFunctionRouteSelection>
        {
            new(
                functionKind,
                selected.InstanceId,
                selected.ModelId,
                selected.ConfigurationGeneration)
        };
        if (_selectedAiFunctionId == AiFunctionId.HigherReasoning)
        {
            if (AiFunctionImageModelSelector.SelectedItem
                is not AiConfiguredModelChoiceVm imageSelected)
            {
                AiFunctionSelectionBoundaryText.Text = "請選擇圖像生成模型。";
                return;
            }
            selections.Add(new D045AiFunctionRouteSelection(
                D045AiFunctionKind.HigherReasoningImageGeneration,
                imageSelected.InstanceId,
                imageSelected.ModelId,
                imageSelected.ConfigurationGeneration));
            selections.Add(new D045AiFunctionRouteSelection(
                D045AiFunctionKind.ComputerActionPlanning,
                selected.InstanceId,
                selected.ModelId,
                selected.ConfigurationGeneration));
        }
        var result = _aiFunctionRoutingStore.SelectMany(
            selections,
            loaded.Snapshot);
        RenderProviderControlPlane();
        AiFunctionSelectionBoundaryText.Text = result.Accepted
            ? "此功能的模型指派已儲存。"
            : "模型指派未更新：" + result.Admission;
        if (!result.Accepted || result.Snapshot == null)
            return;
        AiFunctionAssignmentOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task OpenHermesDefaultModelAssignmentAsync()
    {
        if (_shutdownStarted || HermesManagementPolicy.MutationOrRecoveryInProgress
            || _hermesDefaultModelReading)
            return;

        _selectedAiFunctionId = AiFunctionId.TextConversation;
        AiFunctionDialogTitleText.Text = ControlCenterLocalization.Current.Get("state.hermes.default.model.475");
        AiFunctionDialogDetailText.Text =
            ControlCenterLocalization.Current.Get("state.only.connected.hermes.models.with.available.credentials.are.liste.476")
            + ControlCenterLocalization.Current.Get("state.f6.f7.and.new.chats.use.this.default.individual.chats.can.overrid.477");
        AiFunctionImageModelPanel.Visibility = Visibility.Collapsed;
        AiFunctionApplyButton.Content = ControlCenterLocalization.Current.Get("state.set.as.default.478");
        AiFunctionModelSelector.ItemsSource = _hermesDefaultModelCatalog?.Options
            ?? Array.Empty<D046HermesModelOption>();
        AiFunctionModelSelector.SelectedItem =
            DefaultModelCatalogPresentation.ExactSelection(_hermesDefaultModelCatalog);
        AiFunctionModelSelector.IsEnabled = false;
        AiFunctionApplyButton.IsEnabled = false;
        AiFunctionApplyButton.Visibility = Visibility.Visible;
        AiFunctionOpenProviderSetupButton.Content = ControlCenterLocalization.Current.Get("state.open.api.key.settings.479");
        AiFunctionOpenProviderSetupButton.Visibility = Visibility.Collapsed;
        AiFunctionSelectionBoundaryText.Text = ControlCenterLocalization.Current.Get("state.loading.available.models.from.hermes.480");
        HideProviderOverlays();
        AiFunctionAssignmentOverlay.Visibility = Visibility.Visible;
        var dialogRevision = ++_hermesDefaultModelDialogRevision;
        D046HermesWorkbenchHost? observedHost = null;

        try
        {
            observedHost = GetOrCreateHermesWorkbenchHost();
            var catalog = await LoadHermesDefaultModelCatalogAsync(
                _providerLifetime.Token);
            if (_shutdownStarted || dialogRevision != _hermesDefaultModelDialogRevision
                || !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            AiFunctionModelSelector.ItemsSource = catalog.Options;
            AiFunctionModelSelector.SelectedItem = catalog.HasExactSelection
                ? catalog.Options.Single(option => string.Equals(
                        option.ProviderId,
                        catalog.ExactProviderId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        option.ModelId,
                        catalog.ExactModelId,
                        StringComparison.Ordinal))
                : null; // A missing exact default is not consent to select the first model.
            var canChoose = catalog.Options.Count > 0;
            UpdateHermesManagementControls();
            AiFunctionOpenProviderSetupButton.Visibility = canChoose
                ? Visibility.Collapsed
                : Visibility.Visible;
            AiFunctionSelectionBoundaryText.Text = canChoose
                ? catalog.HasExactSelection
                    ? ControlCenterLocalization.Current.Get("state.this.is.the.native.hermes.default.no.duplicate.model.configuratio.481")
                    : ControlCenterLocalization.Current.Get("state.select.a.model.as.the.native.hermes.default.482")
                : ControlCenterLocalization.Current.Get("state.no.models.connected.set.up.an.api.key.above.first.483");
        }
        catch (OperationCanceledException) when (
            _providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_shutdownStarted || dialogRevision != _hermesDefaultModelDialogRevision
                || observedHost is not null && !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            var failure = Texts.Format("model.default.readFailed",
                DefaultModelCatalogPresentation.FailureCode(exception));
            InvalidateHermesDefaultModelCatalog(failure);
            // A failed read is not evidence of missing credentials. Keep the
            // last rows visible (disabled), and do not direct the player to
            // replace a working key or imply the saved model was cleared.
            AiFunctionOpenProviderSetupButton.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ApplyHermesDefaultModelAssignmentAsync()
    {
        if (_shutdownStarted
            || HermesManagementBusy
            || !_hermesDefaultModelCatalogAuthoritative
            || _hermesDefaultModelCatalog is not { } catalog
            || AiFunctionModelSelector.SelectedItem
                is not D046HermesModelOption selected
            || !catalog.Options.Any(option => string.Equals(
                    option.ProviderId,
                    selected.ProviderId,
                    StringComparison.Ordinal)
                && string.Equals(
                    option.ModelId,
                    selected.ModelId,
                    StringComparison.Ordinal)))
        {
            AiFunctionSelectionBoundaryText.Text =
                ControlCenterLocalization.Current.Get("state.the.model.list.is.stale.close.and.reopen.this.card.486");
            return;
        }

        if (!HasDifferentHermesDefaultSelection())
        {
            AiFunctionSelectionBoundaryText.Text = ControlCenterLocalization.Current.Get("state.modelDefaultUnchanged");
            return;
        }

        SetHermesDefaultModelBusy(true);
        AiFunctionSelectionBoundaryText.Text =
            $"正在將 {selected.DisplayName} 設為預設模型…";
        D046HermesWorkbenchHost? observedHost = null;
        try
        {
            observedHost = GetOrCreateHermesWorkbenchHost();
            var receipt = await observedHost
                .SelectDefaultModelAsync(
                    new D046HermesDefaultModelSelectionRequest(
                        Guid.NewGuid().ToString("N"),
                        catalog.RuntimeGeneration,
                        selected.ProviderId,
                        selected.ModelId,
                        ConfirmExpensiveModel: true),
                    _providerLifetime.Token);
            if (_shutdownStarted || !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            if (!receipt.Accepted)
            {
                if (string.Equals(receipt.Code, "runtime.model_default_unchanged", StringComparison.Ordinal))
                {
                    AiFunctionSelectionBoundaryText.Text = ControlCenterLocalization.Current.Get("state.modelDefaultUnchanged");
                    await LoadHermesDefaultModelCatalogAsync(_providerLifetime.Token);
                    return;
                }
                var failureDetail = string.IsNullOrWhiteSpace(receipt.Warning)
                    ? receipt.Code
                    : receipt.Code + "：" + receipt.Warning;
                if (receipt.ConfirmationRequired)
                {
                    AiFunctionSelectionBoundaryText.Text =
                        ControlCenterLocalization.Current.Get("state.hermes.requires.confirmation.again.reselect.this.model.to.retry.487")
                        + "（" + failureDetail + "）";
                    return;
                }
                if (string.Equals(
                        receipt.Code,
                        "runtime.native_config_transition_busy",
                        StringComparison.Ordinal))
                {
                    AiFunctionSelectionBoundaryText.Text =
                        ControlCenterLocalization.Current.Get("state.hermes.did.not.change.the.default.model.you.may.retry.488")
                        + "（" + failureDetail + "）";
                    return;
                }

                if (string.Equals(receipt.Code, "runtime.model_default_selection_rejected", StringComparison.Ordinal))
                {
                    AiFunctionSelectionBoundaryText.Text = ControlCenterLocalization.Current.Get("state.modelSelectionRejected");
                    return;
                }

                InvalidateHermesDefaultModelCatalog(
                    ControlCenterLocalization.Current.Get("state.default.model.unconfirmed.reconnect.and.reload.from.hermes.489")
                    + " (" + DefaultModelCatalogPresentation.SafeReadFailureCode(receipt.Code) + ")");
                AiFunctionSelectionBoundaryText.Text =
                    "目前無法確認變更結果。請先重新連線並確認目前設定，再嘗試變更。"
                    + "（" + failureDetail + "）";
                return;
            }

            var refreshed = await LoadHermesDefaultModelCatalogAsync(
                _providerLifetime.Token);
            if (!refreshed.HasExactSelection
                || !string.Equals(
                    refreshed.ExactProviderId,
                    selected.ProviderId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    refreshed.ExactModelId,
                    selected.ModelId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes did not publish the selected default model.");
            }

            AiFunctionSelectionBoundaryText.Text = ControlCenterLocalization.Current.Get("state.hermes.default.model.updated.490");
            AiFunctionAssignmentOverlay.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (
            _providerLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_shutdownStarted || observedHost is not null && !ReferenceEquals(observedHost, _hermesWorkbenchHost)) return;
            InvalidateHermesDefaultModelCatalog(
                ControlCenterLocalization.Current.Get("state.default.model.unconfirmed.click.the.card.to.reload.from.hermes.491")
                + " (" + DefaultModelCatalogPresentation.FailureCode(exception) + ")");
            AiFunctionSelectionBoundaryText.Text =
                ControlCenterLocalization.Current.Get("state.change.result.unconfirmed.reopen.the.card.to.reload.from.hermes.492")
                + " (" + DefaultModelCatalogPresentation.FailureCode(exception) + ")";
        }
        finally
        {
            SetHermesDefaultModelBusy(false);
        }
    }

    private bool HasDifferentHermesDefaultSelection()
        => _hermesDefaultModelCatalogAuthoritative
            && _hermesDefaultModelCatalog is { } catalog
            && AiFunctionModelSelector.SelectedItem is D046HermesModelOption option
            && (!catalog.HasExactSelection
                || !string.Equals(catalog.ExactProviderId, option.ProviderId, StringComparison.Ordinal)
                || !string.Equals(catalog.ExactModelId, option.ModelId, StringComparison.Ordinal));

    private void AiFunctionModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedAiFunctionId == AiFunctionId.TextConversation)
            AiFunctionApplyButton.IsEnabled = !HermesManagementBusy && HasDifferentHermesDefaultSelection();
    }

    private void SetHermesDefaultModelBusy(bool busy)
    {
        _hermesDefaultModelBusy = busy;
        UpdateHermesManagementControls();
    }

    private static bool TryMapFunctionKind(
        AiFunctionId functionId,
        out D045AiFunctionKind functionKind)
    {
        functionKind = functionId switch
        {
            AiFunctionId.TextConversation =>
                D045AiFunctionKind.TextConversation,
            AiFunctionId.ComputerActionPlanning =>
                D045AiFunctionKind.ComputerActionPlanning,
            AiFunctionId.HigherReasoning =>
                D045AiFunctionKind.HigherReasoning,
            _ => D045AiFunctionKind.Invalid
        };
        return functionKind != D045AiFunctionKind.Invalid;
    }

    private void OpenConnectedProviderCatalog_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string instanceId })
            return;
        var connected = _aiFunctionsProjection.ConnectedProviders
            .SingleOrDefault(value => string.Equals(
                value.InstanceId,
                instanceId,
                StringComparison.Ordinal));
        if (connected == null)
            return;
        _selectedConnectedInstanceId = connected.InstanceId;
        ProviderCatalogTitleText.Text = connected.DisplayName + " 模型目錄";
        ProviderCatalogSummaryText.Text = connected.CatalogSummary
            + " · "
            + connected.OverallHealthSummary;
        ProviderCatalogList.ItemsSource = connected.Models;
        ProviderManageInstanceButton.IsEnabled = !string.Equals(
            connected.InstanceId,
            "legacy-deepseek-v1",
            StringComparison.Ordinal);
        HideProviderOverlays();
        ProviderCatalogOverlay.Visibility = Visibility.Visible;
    }

    private void OpenSelectedProviderSetup_Click(
        object sender,
        RoutedEventArgs e)
    {
        var connected = _aiFunctionsProjection.ConnectedProviders
            .SingleOrDefault(value => string.Equals(
                value.InstanceId,
                _selectedConnectedInstanceId,
                StringComparison.Ordinal));
        if (connected == null
            || string.Equals(
                connected.InstanceId,
                "legacy-deepseek-v1",
                StringComparison.Ordinal))
        {
            return;
        }
        var profile = UnifiedProviderProfileRegistry.Get(connected.ProviderKind);
        var setup = _aiFunctionsProjection.SetupOptions.Single(value =>
            string.Equals(
                value.ProviderId,
                connected.ProviderId,
                StringComparison.Ordinal));
        OpenProviderSetup(profile, setup, connected.InstanceId);
    }

    private void OpenAddProvider_Click(object sender, RoutedEventArgs e)
    {
        HideProviderOverlays();
        AddProviderOverlay.Visibility = Visibility.Visible;
    }

    private void OpenAddProviderFromFunction_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedAiFunctionId == AiFunctionId.TextConversation)
        {
            AiFunctionAssignmentOverlay.Visibility = Visibility.Collapsed;
            HermesCredentialCard.BringIntoView();
            HermesCredentialProviderList.Focus();
            return;
        }
        AiFunctionOpenProviderSetupButton.Content = ControlCenterLocalization.Current.Get("ui.add.an.ai.provider");
        OpenAddProvider_Click(sender, e);
    }

    private void ProviderSetupCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerId })
            return;
        var setup = _aiFunctionsProjection.SetupOptions.SingleOrDefault(value =>
            string.Equals(value.ProviderId, providerId, StringComparison.Ordinal));
        var profile = UnifiedProviderProfileRegistry.All.SingleOrDefault(value =>
            string.Equals(value.ProviderId, providerId, StringComparison.Ordinal));
        if (setup is not { CanOpenSetup: true } || profile == null)
            return;
        OpenProviderSetup(profile, setup, exactInstanceId: null);
    }

    private void OpenProviderSetup(
        ProviderControlProfile profile,
        AiProviderSetupOptionVm setup,
        string? exactInstanceId)
    {
        _selectedProviderProfile = profile;
        var loaded = _providerStore.Load();
        var instances = loaded.Snapshot?.Instances
            ?? Array.Empty<UnifiedProviderConfiguredInstance>();
        var existing = string.IsNullOrEmpty(exactInstanceId)
            ? null
            : instances.SingleOrDefault(value => string.Equals(
                value.InstanceId,
                exactInstanceId,
                StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(exactInstanceId) && existing == null)
            return;
        if (existing != null && existing.ProviderKind != profile.Kind)
            return;
        var connected = existing == null
            ? null
            : _aiFunctionsProjection.ConnectedProviders.SingleOrDefault(value =>
                string.Equals(
                    value.InstanceId,
                    existing.InstanceId,
                    StringComparison.Ordinal));
        _selectedConnectedInstanceId = existing?.InstanceId ?? string.Empty;

        ProviderSetupTitleText.Text = profile.DisplayName + " 安全設定";
        ProviderSetupProtocolText.Text = profile.Protocol
            + " · profile "
            + profile.ProviderId;
        ProviderInstanceIdInput.Text = existing?.InstanceId
            ?? NextProviderInstanceId(profile.ProviderId, instances);
        ProviderInstanceIdInput.IsReadOnly = existing != null;
        ProviderWorkspaceInput.Clear();

        ProviderEndpointProfilePanel.Visibility =
            profile.RequiresExplicitEndpointProfile
                ? Visibility.Visible
                : Visibility.Collapsed;
        ProviderEndpointProfileSelector.ItemsSource =
            profile.EndpointProfiles.Select(value => value.ProfileId).ToArray();
        ProviderEndpointProfileSelector.SelectedItem = existing?.EndpointProfileId
            ?? profile.EndpointProfiles.FirstOrDefault()?.ProfileId;
        ProviderWorkspaceInput.Text = existing?.WorkspaceId ?? string.Empty;

        ProviderBaseUrlInput.Text = existing?.BaseUri.AbsoluteUri
            ?? profile.DefaultBaseUri?.AbsoluteUri
            ?? string.Empty;
        ProviderBaseUrlInput.IsReadOnly = !profile.PlayerMayEditEndpoint
            || profile.RequiresExplicitEndpointProfile;
        var routeChoices = ProviderControlCenterRouteChoices.For(profile.Kind);
        ProviderRouteIntentPanel.Visibility = routeChoices.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        ProviderRouteIntentSelector.ItemsSource = routeChoices;
        var selectedRouteKind = existing?.RouteIntent.Kind
            ?? ConfiguredEndpointRouteKind.LoopbackOnly;
        ProviderRouteIntentSelector.SelectedItem = routeChoices.SingleOrDefault(value =>
            value.Kind == selectedRouteKind)
            ?? routeChoices.FirstOrDefault();
        ProviderEndpointPolicyText.Text = profile.PlayerMayEditEndpoint
            ? "只使用玩家明確指定的 Base URL；不掃描區網、不猜測平台，也不切換到替代端點。"
            : "此平台使用固定官方端點；不接受替代 host、跨區嘗試或 Key 來源猜測。";

        ProviderCredentialPanel.Visibility = Visibility.Collapsed;
        ProviderCredentialHintText.Text = profile.CredentialRequirement
            == ProviderCredentialRequirement.Required
                ? "此平台需要憑證。憑證只可送往目前明確選定的端點，不跨平台探測。"
                : "憑證為選用；是否需要由這個明確設定的端點決定，不自動猜測。";
        ProviderApiKeyInput.Clear();

        ProviderModelSelector.ItemsSource = existing?.CatalogModels
            ?? Array.Empty<string>();
        ProviderModelSelector.Text = existing?.SelectedModel ?? string.Empty;
        ProviderModelSelector.IsEnabled = _providerNetworkActionsEnabled
            || existing != null;
        ProviderApplyModelButton.IsEnabled = existing != null;

        ProviderCredentialLayerText.Text = connected?.CredentialHealthSummary
            ?? "憑證：尚未設定";
        ProviderCatalogLayerText.Text = connected?.CatalogHealthSummary
            ?? ControlCenterLocalization.Current.Get("ui.catalog.not.verified");
        ProviderRuntimeLayerText.Text = connected?.RuntimeHealthSummary
            ?? ControlCenterLocalization.Current.Get("ui.runtime.not.verified");
        SetProviderHealth(
            connected?.OverallHealthSummary ?? ControlCenterLocalization.Current.Get("ui.not.configured"),
            connected?.HealthColor ?? "#52627A");

        var legacyOnly = profile.Kind == ProviderControlKind.DeepSeek
            && existing == null
            && _aiFunctionsProjection.ConnectedProviders.Any(value =>
                string.Equals(
                    value.InstanceId,
                    "legacy-deepseek-v1",
                    StringComparison.Ordinal));
        ProviderOperationStatusText.Text = !_providerNetworkActionsEnabled
            ? "D-033 Source Preview：可檢視精確設定欄位，但真實驗證、憑證輸入與網路請求目前停用。"
            : legacyOnly
                ? "偵測到 Legacy DeepSeek；此處只讀相容，不會由新 UI 寫回舊 store。D-033 驗證接線完成後可建立新的原子設定世代。"
                : "D-033：目前只準備精確設定草稿，尚未送出網路請求或保存未驗證資料。";

        UpdateEndpointProfileFields();
        UpdateRouteIntentWarning();
        HideProviderOverlays();
        ProviderConnectionOverlay.Visibility = Visibility.Visible;
    }

    private static string NextProviderInstanceId(
        string providerId,
        IEnumerable<UnifiedProviderConfiguredInstance> instances)
    {
        var used = new HashSet<string>(
            instances.Select(value => value.InstanceId),
            StringComparer.Ordinal);
        var preferred = providerId + "-default";
        if (!used.Contains(preferred))
            return preferred;
        for (var suffix = 2;
             suffix <= UnifiedProviderControlPlaneStore.MaximumInstances + 1;
             suffix++)
        {
            var candidate = providerId + "-" + suffix;
            if (!used.Contains(candidate))
                return candidate;
        }
        return providerId + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    private void ProviderEndpointProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
        => UpdateEndpointProfileFields();

    private void ProviderWorkspaceInput_TextChanged(
        object sender,
        TextChangedEventArgs e)
        => UpdateEndpointProfileFields();

    private void ProviderRouteIntentSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
        => UpdateRouteIntentWarning();

    private void UpdateRouteIntentWarning()
    {
        ProviderRouteIntentWarningText.Text =
            ProviderRouteIntentSelector.SelectedItem
                is ProviderControlCenterRouteChoice
                {
                    Kind: ConfiguredEndpointRouteKind.LoopbackOnly
                }
                ? "本機模式只接受 Loopback；不掃描、不猜測其他端點。"
                : "指定區網或遠端必須使用 HTTPS；不掃描、不猜測，且送出時仍需以連線位址證據重新驗證。";
    }

    private void UpdateEndpointProfileFields()
    {
        var profile = _selectedProviderProfile;
        if (profile == null || !profile.RequiresExplicitEndpointProfile)
        {
            ProviderWorkspacePanel.Visibility = Visibility.Collapsed;
            ClearHiddenWorkspace();
            return;
        }
        var endpoint = profile.EndpointProfiles.SingleOrDefault(value =>
            string.Equals(
                value.ProfileId,
                ProviderEndpointProfileSelector.SelectedItem as string,
                StringComparison.Ordinal));
        if (endpoint == null)
        {
            ProviderWorkspacePanel.Visibility = Visibility.Collapsed;
            ClearHiddenWorkspace();
            return;
        }
        ProviderWorkspacePanel.Visibility = endpoint.RequiresWorkspaceId
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!endpoint.RequiresWorkspaceId)
            ClearHiddenWorkspace();
        var workspace = string.IsNullOrWhiteSpace(ProviderWorkspaceInput.Text)
            ? ProviderEndpointProfile.WorkspacePlaceholder
            : ProviderWorkspaceInput.Text.Trim();
        ProviderBaseUrlInput.Text = endpoint.BaseAddressTemplate.Replace(
            ProviderEndpointProfile.WorkspacePlaceholder,
            workspace,
            StringComparison.Ordinal);
    }

    private void ClearHiddenWorkspace()
    {
        if (ProviderWorkspaceInput.Text.Length != 0)
            ProviderWorkspaceInput.Clear();
    }

    private void ProviderPrepareValidationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProviderApiKeyInput.Clear();
        ProviderOperationStatusText.Text =
            "這個舊入口已停用。請回到「AI 與工具」頁面上方管理 API Key。";
    }

    private bool RefreshProviderSetupFields(string instanceId)
    {
        var snapshot = _providerStore.Load().Snapshot;
        var instance = snapshot?.Instances.SingleOrDefault(value =>
            string.Equals(value.InstanceId, instanceId, StringComparison.Ordinal));
        if (instance == null)
            return false;
        _selectedConnectedInstanceId = instance.InstanceId;
        ProviderInstanceIdInput.Text = instance.InstanceId;
        ProviderInstanceIdInput.IsReadOnly = true;
        ProviderModelSelector.ItemsSource = instance.CatalogModels;
        ProviderModelSelector.Text = instance.SelectedModel;
        ProviderModelSelector.IsEnabled = true;
        ProviderApplyModelButton.IsEnabled = true;
        var connected = _aiFunctionsProjection.ConnectedProviders.SingleOrDefault(value =>
            string.Equals(value.InstanceId, instance.InstanceId, StringComparison.Ordinal));
        if (connected != null)
        {
            ProviderCredentialLayerText.Text = connected.CredentialHealthSummary;
            ProviderCatalogLayerText.Text = connected.CatalogHealthSummary;
            ProviderRuntimeLayerText.Text = connected.RuntimeHealthSummary;
            SetProviderHealth(
                connected.OverallHealthSummary,
                connected.HealthColor);
        }
        return true;
    }

    private static bool TryManualModel(string? value, out string modelId)
    {
        modelId = value?.Trim() ?? string.Empty;
        return modelId.Length is > 0
            and <= UnifiedProviderControlPlaneStore.MaximumModelIdCharacters
            && !modelId.Any(char.IsControl);
    }

    private void ProviderApplyModelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedConnectedInstanceId)
            || !TryManualModel(ProviderModelSelector.Text, out var modelId))
        {
            return;
        }
        var snapshot = _providerStore.Load().Snapshot;
        var instance = snapshot?.Instances.SingleOrDefault(value =>
            string.Equals(
                value.InstanceId,
                _selectedConnectedInstanceId,
                StringComparison.Ordinal));
        if (instance == null)
            return;
        var result = _providerStore.SelectTextModel(
            instance.InstanceId,
            modelId,
            instance.ConfigurationGeneration,
            DateTimeOffset.UtcNow);
        RenderProviderControlPlane();
        ProviderOperationStatusText.Text = result.Accepted
            ? "Desired 文字模型已更新；這不會啟用遊戲端 Provider。"
            : "模型未更新：" + result.Admission;
    }

    private void SetProviderHealth(string text, string color)
    {
        ProviderSetupHealthText.Text = text;
        BindingOperations.SetBinding(ProviderSetupHealthText, TextBlock.ForegroundProperty,
            ThemeBrushExtension.CreateBinding(new Binding { Source = color }));
        BindingOperations.SetBinding(ProviderSetupHealthLight, System.Windows.Shapes.Shape.FillProperty,
            ThemeBrushExtension.CreateBinding(new Binding { Source = color }));
    }

    private static string ProviderBoundaryStatusText(
        ProviderControlCenterNetworkBoundaryReason reason,
        ProviderControlCenterResultAdmission admission,
        int acceptedObservationCount = 0)
    {
        if (admission == ProviderControlCenterResultAdmission.Accepted)
        {
            return acceptedObservationCount > 0
                ? $"已接受 {acceptedObservationCount} 筆身分、世代與時效皆吻合的連線結果。"
                : "已接受身分、世代與時效皆吻合的設定結果。";
        }
        return admission switch
        {
            ProviderControlCenterResultAdmission.RequestIdentityMismatch =>
                "回傳的 request identity 不符；結果未套用。",
            ProviderControlCenterResultAdmission.OperationMismatch =>
                "回傳的操作類型不符；結果未套用。",
            ProviderControlCenterResultAdmission.ObservationShapeMismatch =>
                "回傳資料結構不符；結果未套用。",
            ProviderControlCenterResultAdmission.InstanceIdentityMismatch =>
                "回傳的 configured instance identity 不符；結果未套用。",
            ProviderControlCenterResultAdmission.ConfigurationGenerationMismatch =>
                "設定世代已改變；舊結果未套用。",
            ProviderControlCenterResultAdmission.ConfigurationFingerprintMismatch =>
                "設定內容已改變；舊結果未套用。",
            ProviderControlCenterResultAdmission.CurrentInstanceMissing =>
                "目前找不到對應的已設定實例；結果未套用。",
            ProviderControlCenterResultAdmission.FreshnessRejected =>
                "連線結果已過期或尚未生效；結果未套用。",
            ProviderControlCenterResultAdmission.HealthReasonMismatch =>
                "連線狀態與原因不相符；結果未套用。",
            ProviderControlCenterResultAdmission.StoreGenerationMismatch =>
                "設定儲存世代已改變；結果未套用。",
            ProviderControlCenterResultAdmission.ModelMismatch =>
                "模型識別不符；結果未套用。",
            ProviderControlCenterResultAdmission.EndpointMismatch =>
                "端點設定不符；結果未套用。",
            ProviderControlCenterResultAdmission.AuthenticationMismatch =>
                "憑證設定不符；結果未套用。",
            ProviderControlCenterResultAdmission.RouteIntentMismatch =>
                "端點路由意圖不符；結果未套用。",
            _ => ProviderBoundaryReasonText(reason)
        };
    }

    private static string ProviderBoundaryReasonText(
        ProviderControlCenterNetworkBoundaryReason reason)
        => reason switch
        {
            ProviderControlCenterNetworkBoundaryReason.SourcePredeploymentDisabled =>
                "尚未授權真實連線，因此沒有送出探測。",
            ProviderControlCenterNetworkBoundaryReason.Completed =>
                "連線邊界已完成，但結果未通過本機套用條件。",
            ProviderControlCenterNetworkBoundaryReason.CallerCancelled =>
                "連線檢查已取消。",
            ProviderControlCenterNetworkBoundaryReason.CapacityUnavailable =>
                "連線檢查容量已滿，沒有重試。",
            ProviderControlCenterNetworkBoundaryReason.EndpointRejected =>
                "端點遭安全規則拒絕。",
            ProviderControlCenterNetworkBoundaryReason.AuthenticationRejected =>
                "憑證遭服務拒絕。",
            ProviderControlCenterNetworkBoundaryReason.RateLimited =>
                "服務暫時限制請求頻率。",
            ProviderControlCenterNetworkBoundaryReason.NetworkUnavailable =>
                "目前無法連線。",
            ProviderControlCenterNetworkBoundaryReason.TimedOut =>
                "連線檢查逾時，沒有重試。",
            ProviderControlCenterNetworkBoundaryReason.ProtocolRejected =>
                "回傳內容不符合所選 Provider 協定。",
            ProviderControlCenterNetworkBoundaryReason.StaleResult =>
                "連線結果已過期。",
            ProviderControlCenterNetworkBoundaryReason.IdentityMismatch =>
                "連線結果身分不符。",
            ProviderControlCenterNetworkBoundaryReason.Faulted =>
                "連線邊界發生受控錯誤。",
            _ => "連線狀態無法判定。"
        };

    private static string ProviderBoundaryReasonColor(
        ProviderControlCenterNetworkBoundaryReason reason)
        => reason == ProviderControlCenterNetworkBoundaryReason.SourcePredeploymentDisabled
            ? "#52627A"
            : reason is ProviderControlCenterNetworkBoundaryReason.EndpointRejected
            or ProviderControlCenterNetworkBoundaryReason.AuthenticationRejected
            or ProviderControlCenterNetworkBoundaryReason.ProtocolRejected
            or ProviderControlCenterNetworkBoundaryReason.IdentityMismatch
            ? "#BD354B"
            : reason == ProviderControlCenterNetworkBoundaryReason.Completed
                ? "#1D795A"
                : "#936110";

    private static string ProviderHealthReasonText(
        ProviderControlCenterHealthReason reason)
        => reason switch
        {
            ProviderControlCenterHealthReason.CatalogReachable =>
                "目錄連線成功",
            ProviderControlCenterHealthReason.GenerationVerified =>
                "文字回覆已驗證",
            ProviderControlCenterHealthReason.AuthenticationRejected =>
                "憑證遭拒",
            ProviderControlCenterHealthReason.EndpointPolicyRejected =>
                "端點遭安全規則拒絕",
            ProviderControlCenterHealthReason.RateLimited =>
                "暫時受速率限制",
            ProviderControlCenterHealthReason.NetworkUnavailable =>
                "目前無法連線",
            ProviderControlCenterHealthReason.TimedOut =>
                "連線檢查逾時",
            ProviderControlCenterHealthReason.ProtocolRejected =>
                "回傳協定不符",
            ProviderControlCenterHealthReason.ObservationUnsupported =>
                "此 Provider 不支援目前的連線檢查",
            ProviderControlCenterHealthReason.Cancelled =>
                "連線檢查已取消",
            _ => "連線狀態無法判定"
        };

    private static string ProviderHealthReasonColor(
        ProviderConnectionHealth health)
        => health is ProviderConnectionHealth.CatalogAvailable
            or ProviderConnectionHealth.GenerationVerified
            ? "#1D795A"
            : health is ProviderConnectionHealth.AuthenticationRejected
                or ProviderConnectionHealth.EndpointRejected
                ? "#BD354B"
                : "#936110";

    private void CloseAiFunctionAssignment_Click(
        object sender,
        RoutedEventArgs e)
    {
        _hermesDefaultModelDialogRevision++;
        AiFunctionAssignmentOverlay.Visibility = Visibility.Collapsed;
    }

    private void CloseProviderCatalog_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProviderCatalogOverlay.Visibility = Visibility.Collapsed;
        _selectedConnectedInstanceId = string.Empty;
    }

    private void CloseAddProvider_Click(object sender, RoutedEventArgs e)
        => AddProviderOverlay.Visibility = Visibility.Collapsed;

    private void CloseProviderSetup_Click(object sender, RoutedEventArgs e)
    {
        ProviderApiKeyInput.Clear();
        ProviderConnectionOverlay.Visibility = Visibility.Collapsed;
        _selectedProviderProfile = null;
        _selectedConnectedInstanceId = string.Empty;
    }

    private void HideProviderOverlays()
    {
        _hermesDefaultModelDialogRevision++;
        if (HermesNativeToolsOverlay.Visibility == Visibility.Visible)
            _hermesWorkbenchHost?.ViewModel.CancelNativeToolsChanges();
        AiFunctionAssignmentOverlay.Visibility = Visibility.Collapsed;
        HermesCapabilityCatalogOverlay.Visibility = Visibility.Collapsed;
        HermesNativeToolsOverlay.Visibility = Visibility.Collapsed;
        ProviderCatalogOverlay.Visibility = Visibility.Collapsed;
        AddProviderOverlay.Visibility = Visibility.Collapsed;
        ProviderConnectionOverlay.Visibility = Visibility.Collapsed;
        ProviderApiKeyInput.Clear();
    }

    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0)
            return;
        try
        {
            var response = await ControlCenterReadOnlyClient.ReadOnceAsync(
                CancellationToken.None);
            DataContext = ControlCenterReadOnlyProjection.From(response.Status!);
        }
        catch (Exception exception)
        {
            DataContext = ControlCenterReadOnlyProjection.Offline(
                ControlCenterReadOnlyProjection.OfflineReasonForReadFailure(exception));
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

}
