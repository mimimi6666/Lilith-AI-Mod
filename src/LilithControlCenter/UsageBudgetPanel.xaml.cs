using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LilithControlCenter;

public partial class UsageBudgetPanel : UserControl
{
    internal UsageBudgetViewModel Model { get; } = new();
    // A test seam only narrows motion; it can never override the Windows
    // accessibility preference to force animations on.
    internal bool SuppressAnimations { get; set; }
    private bool UseAnimations => !SuppressAnimations && SystemParameters.ClientAreaAnimation;
    private bool _visualReady;
    private readonly DispatcherTimer _commitTimer;
    public UsageBudgetPanel()
    {
        InitializeComponent();
        BudgetContent.DataContext = Model;
        _commitTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(400) };
        _commitTimer.Tick += CommitTimer_Tick;
        Loaded += (_, _) => { ControlCenterLocalization.Current.LanguageChanged += Model.RefreshLanguage; Model.RefreshLanguage(); UpdateSliderVisual(false); };
        Unloaded += (_, _) => { _commitTimer.Stop(); UpdateSliderVisual(false); _visualReady = false; ControlCenterLocalization.Current.LanguageChanged -= Model.RefreshLanguage; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _commitTimer.Stop(); };
    }
    public void Attach(IUsageBudgetProjection projection) => Model.Attach(projection);
    public void Detach() { _commitTimer.Stop(); Model.Detach(); }
    public Task RefreshAsync() { _commitTimer.Stop(); return Model.RefreshAsync(); }
    internal Task ObserveAsync() => Model.IsDirty || Model.IsBusy || IsKeyboardFocusWithin
        ? Task.CompletedTask : Model.RefreshAsync(background: true);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderVisual(_visualReady && IsVisible);
        // The binding only previews. Pointer drag never sends per tick; keyboard
        // and accessibility changes coalesce after a quiet interval.
        if (_commitTimer is null || !Model.CanSave) return;
        _commitTimer.Stop();
        if (Mouse.LeftButton != MouseButtonState.Pressed && IsVisible) _commitTimer.Start();
    }
    private async void Slider_Released(object sender, MouseButtonEventArgs e)
    { _commitTimer.Stop(); await Model.SaveAsync(); }
    private async void Slider_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { _commitTimer.Stop(); if (IsVisible) await Model.SaveAsync(); }
    private async void CommitTimer_Tick(object? sender, EventArgs e)
    { _commitTimer.Stop(); if (IsVisible) await Model.SaveAsync(); }

    private void Slider_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSliderVisual(false);

    private void UpdateSliderVisual(bool animate)
    {
        if (BudgetSlider?.Template is null) return;
        BudgetSlider.ApplyTemplate();
        if (BudgetSlider.Template.FindName("BudgetRail", BudgetSlider) is not FrameworkElement rail
            || BudgetSlider.Template.FindName("BudgetProgress", BudgetSlider) is not FrameworkElement progress
            || BudgetSlider.Template.FindName("BudgetVisualThumb", BudgetSlider) is not FrameworkElement thumb
            || BudgetSlider.ActualWidth < 32) return;
        var fraction = Math.Clamp((BudgetSlider.Value - 1) / 5, 0, 1);
        // The parent's SizeChanged can precede the template child's arrange.
        // Use the slider's new width so resizing cannot leave the thumb at a stale endpoint.
        var position = (BudgetSlider.ActualWidth - 32) * fraction;
        animate &= UseAnimations;
        AnimateValue(thumb, Canvas.LeftProperty, position, animate);
        AnimateValue(progress, WidthProperty, position, animate);
        if (BudgetLevelText is not null)
        {
            AnimateValue(BudgetLevelText, OpacityProperty, 1, animate, 0.45);
            if (BudgetLevelText.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0;
                if (animate) transform.BeginAnimation(TranslateTransform.YProperty,
                    Ease(5, 0), HandoffBehavior.SnapshotAndReplace);
            }
        }
        for (var level = 1; level <= 6; level++)
            if (FindName("BudgetTick" + level) is TextBlock tick)
            {
                var selected = level == (int)Math.Round(BudgetSlider.Value);
                tick.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                tick.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentStrongBrush" : "MutedBrush");
            }
        _visualReady = true;
    }

    private static void AnimateValue(FrameworkElement element, DependencyProperty property,
        double target, bool animate, double? from = null)
    {
        var current = from ?? (double)element.GetValue(property);
        element.BeginAnimation(property, null);
        element.SetValue(property, target);
        if (animate && double.IsFinite(current) && Math.Abs(current - target) > 0.001)
            element.BeginAnimation(property, Ease(current, target), HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation Ease(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(220))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.Stop
    };
}
