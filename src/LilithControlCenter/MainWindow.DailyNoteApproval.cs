using LilithRuntimeContracts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LilithControlCenter;

public partial class MainWindow
{
    private ID059DailyNoteApprovalProjection? _dailyApprovalProjection;
    private D059DailyNoteApprovalState? _dailyApprovalState;
    private DispatcherTimer? _dailyApprovalTimer;
    private bool _dailyPresenceInFlight;

    private void AttachDailyNoteApproval(D046HermesWorkbenchHost host)
    {
        var projection = host.DailyNoteApprovalProjection;
        if (projection is null || ReferenceEquals(projection, _dailyApprovalProjection)) return;
        DetachDailyNoteApprovalHandlers();
        _dailyApprovalProjection = projection;
        _dailyApprovalState = new D059DailyNoteApprovalState();
        projection.DailyNoteApprovalRequested += DailyNoteApprovalRequested;
        projection.DailyNoteApprovalClosed += DailyNoteApprovalClosed;
        _dailyApprovalTimer ??= CreateDailyNoteApprovalTimer();
        _dailyApprovalTimer.Start();
    }

    private DispatcherTimer CreateDailyNoteApprovalTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await PublishDailyNotePresenceAsync();
        return timer;
    }

    private bool CanReceiveDailyNoteApproval => IsLoaded && IsVisible && WindowState != WindowState.Minimized
        && !_shutdownStarted && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished;

    private async Task PublishDailyNotePresenceAsync()
    {
        if (_dailyPresenceInFlight || _dailyApprovalProjection is not { } projection || _dailyApprovalState is not { } state) return;
        state.Expire(); RenderDailyNoteApproval();
        if (!projection.DailyNoteApprovalChannelAvailable)
        { state.FailClosed(); RenderDailyNoteApproval(); return; }
        // A preconnected pipe is not UI readiness. This callback itself runs on
        // the live Dispatcher and declares only a currently answerable surface.
        if (projection.RuntimeGeneration == Guid.Empty) return;
        _dailyPresenceInFlight = true;
        try
        {
            var generation = projection.RuntimeGeneration;
            var request = state.CreatePresence(generation, CanReceiveDailyNoteApproval);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var receipt = await projection.PublishDailyNoteUiPresenceAsync(request, cancellation.Token);
            if (!ReferenceEquals(projection, _dailyApprovalProjection) || !ReferenceEquals(state, _dailyApprovalState)) return;
            if (!CanReceiveDailyNoteApproval && request.Attached) { state.FailClosed(); return; }
            state.ApplyPresence(generation, request, receipt);
        }
        catch { if (ReferenceEquals(state, _dailyApprovalState)) state.FailClosed(); }
        finally { _dailyPresenceInFlight = false; RenderDailyNoteApproval(); }
    }

    private void DailyNoteApprovalRequested(object? sender, LilithDailyNoteApprovalRequest request)
        => DispatchDailyNoteApproval(() =>
        {
            if (!ReferenceEquals(sender, _dailyApprovalProjection) || !CanReceiveDailyNoteApproval) return;
            _dailyApprovalState?.Receive(_dailyApprovalProjection!.RuntimeGeneration, request);
            RenderDailyNoteApproval();
        });

    private void DailyNoteApprovalClosed(object? sender, LilithDailyNoteApprovalClosed closed)
        => DispatchDailyNoteApproval(() =>
        {
            if (!ReferenceEquals(sender, _dailyApprovalProjection)) return;
            _dailyApprovalState?.Close(_dailyApprovalProjection!.RuntimeGeneration, closed);
            RenderDailyNoteApproval();
        });

    private void DispatchDailyNoteApproval(Action action)
    {
        if (_shutdownStarted || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }

    private async void DailyNoteApprovalChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string choice } || !CanReceiveDailyNoteApproval
            || _dailyApprovalProjection is not { } projection || _dailyApprovalState is not { } state
            || !state.CanRespond(choice)) return;
        try
        {
            var request = state.BeginResponse(choice);
            RenderDailyNoteApproval();
            var receipt = await projection.RespondDailyNoteApprovalAsync(request, _providerLifetime.Token);
            if (ReferenceEquals(state, _dailyApprovalState)) state.CompleteResponse(request, receipt);
        }
        catch { if (ReferenceEquals(state, _dailyApprovalState)) state.FailClosed(); }
        finally { RenderDailyNoteApproval(); }
    }

    private void RenderDailyNoteApproval()
    {
        var state = _dailyApprovalState;
        state?.Expire();
        var pending = state?.Pending;
        DailyNoteApprovalCard.Visibility = pending is not null && CanReceiveDailyNoteApproval ? Visibility.Visible : Visibility.Collapsed;
        DailyNoteApprovalDescription.Text = pending?.Description ?? string.Empty;
        DailyNoteApprovalCommand.Text = pending?.Command ?? string.Empty;
        DailyNoteApprovalStatus.Text = state?.Status ?? string.Empty;
        foreach (var button in new[] { DailyNoteAllowOnce, DailyNoteAllowSession, DailyNoteAllowAlways, DailyNoteDeny })
        {
            var choice = (string)button.Tag;
            button.Visibility = pending?.Choices.Contains(choice, StringComparer.Ordinal) == true ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = state?.CanRespond(choice) == true;
        }
    }

    private async Task StopDailyNoteApprovalAsync(D046HermesWorkbenchHost? expectedHost = null)
    {
        var projection = _dailyApprovalProjection;
        if (expectedHost is not null && !ReferenceEquals(expectedHost.DailyNoteApprovalProjection, projection)) return;
        var state = _dailyApprovalState;
        _dailyApprovalTimer?.Stop();
        // Clear the card synchronously before any await or shutdown teardown.
        state?.FailClosed();
        DetachDailyNoteApprovalHandlers();
        RenderDailyNoteApproval();
        if (projection is null || state is null || projection.RuntimeGeneration == Guid.Empty) return;
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var request = state.CreatePresence(projection.RuntimeGeneration, attached: false);
            await projection.PublishDailyNoteUiPresenceAsync(request, cancellation.Token).WaitAsync(cancellation.Token);
        }
        catch { /* Connection teardown and the five-second lease also revoke. */ }
    }

    private void DetachDailyNoteApprovalHandlers()
    {
        if (_dailyApprovalProjection is { } projection)
        {
            projection.DailyNoteApprovalRequested -= DailyNoteApprovalRequested;
            projection.DailyNoteApprovalClosed -= DailyNoteApprovalClosed;
        }
        _dailyApprovalProjection = null; _dailyApprovalState = null;
    }
}
