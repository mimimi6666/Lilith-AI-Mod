using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LilithControlCenter;

/// <summary>Visible Home owns this short-lived local observation; no Runtime dependency.</summary>
public partial class HomeResourceMonitor : UserControl
{
    private readonly ResourceMonitorViewModel _viewModel = new();
    private readonly Func<IResourceSampler> _samplerFactory;
    private IResourceSampler? _sampler;
    private Window? _window;
    private CancellationTokenSource? _runCancellation;
    private Task? _run;
    private long _generation;
    private bool _active;
    private bool _disposed;

    public HomeResourceMonitor() : this(() => new WindowsResourceSampler()) { }

    internal HomeResourceMonitor(Func<IResourceSampler> samplerFactory)
    {
        _samplerFactory = samplerFactory;
        InitializeComponent();
        ResourceContent.DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    private bool CanObserve => !_disposed && IsLoaded && IsVisible
        && _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        var host = Window.GetWindow(this);
        if (!ReferenceEquals(_window, host))
        {
            Detach();
            _window = host;
            if (_window is not null)
            {
                _window.StateChanged += OnWindowStateChanged;
                _window.Closed += OnWindowClosed;
            }
        }
        ControlCenterLocalization.Current.LanguageChanged += OnLanguageChanged;
        UpdateActivity();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopObservation();
        ControlCenterLocalization.Current.LanguageChanged -= OnLanguageChanged;
        // Keep one sampler and the host's Closed handler across unload/reload.
        // Recreating samplers here could multiply a hung native worker.
    }

    private void Detach()
    {
        ControlCenterLocalization.Current.LanguageChanged -= OnLanguageChanged;
        if (_window is null) return;
        _window.StateChanged -= OnWindowStateChanged;
        _window.Closed -= OnWindowClosed;
        _window = null;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateActivity();
    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateActivity();
    private void OnLanguageChanged() => _viewModel.RefreshLanguage();
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _disposed = true;
        StopObservation();
        Detach();
        _sampler?.Dispose();
        _sampler = null;
    }

    private void UpdateActivity()
    {
        if (!CanObserve) { StopObservation(); return; }
        if (!_active)
        {
            _active = true;
            _generation++;
            _viewModel.Begin();
        }
        StartObservation();
    }

    private void StartObservation()
    {
        if (!CanObserve || !_active || _run is { IsCompleted: false }) return;
        _sampler ??= _samplerFactory(); // The constructor does not query Windows.
        _sampler.Reset();
        _runCancellation = new CancellationTokenSource();
        _run = ObserveAsync(_sampler, _generation, _runCancellation);
    }

    private void StopObservation()
    {
        if (!_active) return;
        _active = false;
        _generation++;
        _runCancellation?.Cancel();
        _viewModel.Pause();
    }

    private async Task ObserveAsync(IResourceSampler sampler, long generation, CancellationTokenSource cancellation)
    {
        // Never delay the initial layout for the first observation.
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            while (!cancellation.IsCancellationRequested && CanObserve && generation == _generation)
            {
                var delay = TimeSpan.FromSeconds(2);
                try
                {
                    var snapshot = await sampler.ReadAsync(cancellation.Token);
                    if (cancellation.IsCancellationRequested || !CanObserve || generation != _generation) break;
                    _viewModel.Apply(snapshot);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    // A transient sampler failure must not permanently stop this
                    // visible panel. Reuse the same sampler and back off; never
                    // multiply native workers or publish stale values as current.
                    if (generation == _generation) _viewModel.Pause();
                    delay = TimeSpan.FromSeconds(5);
                }
                await Task.Delay(delay, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            // Optional metrics cannot take down the control surface or block navigation.
            if (generation == _generation) _viewModel.Pause();
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_runCancellation, cancellation)) _runCancellation = null;
            _run = null;
            // A hide/show during an unfinished read must not create concurrent readers.
            if (CanObserve && _active && generation != _generation)
                _ = Dispatcher.BeginInvoke(StartObservation, DispatcherPriority.Background);
        }
    }
}
