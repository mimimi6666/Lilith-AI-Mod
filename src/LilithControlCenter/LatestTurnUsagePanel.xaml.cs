using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LilithControlCenter;

public partial class LatestTurnUsagePanel : UserControl
{
    internal LatestTurnUsageViewModel Model { get; } = new();
    private ILatestTurnUsageProjection? _projection;
    private CancellationTokenSource? _cancellation;
    private Task? _run;
    private Window? _window;
    private long _generation;
    private bool _unsupported;
    public LatestTurnUsagePanel()
    {
        InitializeComponent();
        LatestUsageContent.DataContext = Model;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateObservation();
    }
    public void Attach(ILatestTurnUsageProjection projection)
    {
        if (ReferenceEquals(_projection, projection)) return;
        Detach(); _projection = projection; _unsupported = false; UpdateObservation();
    }
    public void Detach()
    {
        _projection = null; _generation++; _cancellation?.Cancel(); _unsupported = false; Model.Reset();
    }
    private bool CanObserve => IsLoaded && IsVisible && !_unsupported && _projection is not null
        && _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized;
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        ControlCenterLocalization.Current.LanguageChanged += Model.RefreshLanguage;
        UpdateObservation();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _generation++; _cancellation?.Cancel();
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = null;
        ControlCenterLocalization.Current.LanguageChanged -= Model.RefreshLanguage;
    }
    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateObservation();
    private void UpdateObservation()
    {
        if (!CanObserve) { _generation++; _cancellation?.Cancel(); return; }
        if (_run is { IsCompleted: false }) return;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _run = ObserveAsync(_projection!, _generation, cancellation);
    }
    private async Task ObserveAsync(ILatestTurnUsageProjection projection, long generation, CancellationTokenSource cancellation)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            while (CanObserve && generation == _generation && !cancellation.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await projection.ReadLatestTurnUsageAsync(cancellation.Token);
                    if (!CanObserve || generation != _generation || cancellation.IsCancellationRequested) break;
                    Model.Apply(snapshot);
                    if (snapshot is null) { _unsupported = true; break; }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (generation == _generation) Model.Unavailable(); }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose(); _run = null;
            if (CanObserve && generation != _generation) _ = Dispatcher.BeginInvoke(UpdateObservation, DispatcherPriority.Background);
        }
    }
}
