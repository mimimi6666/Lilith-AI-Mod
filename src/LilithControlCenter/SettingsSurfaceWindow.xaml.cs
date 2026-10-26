using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace LilithControlCenter;

// A presentation-only host for an existing editor. It owns no transport,
// catalog, setting, key, or retry policy. The editor and its caller stay unique.
public partial class SettingsSurfaceWindow : Window
{
    private readonly Func<bool> _operationInProgress;
    private readonly string? _titleKey;
    private readonly DispatcherTimer _closeFeedbackTimer;
    private bool _ownerShuttingDown;

    public SettingsSurfaceWindow(FrameworkElement editor, string title, Func<bool> operationInProgress,
        string? titleKey = null)
    {
        InitializeComponent();
        _operationInProgress = operationInProgress;
        _titleKey = titleKey;
        // This only observes the caller's in-memory busy bit after a refused
        // close; it does no status read, setting write, or network polling.
        _closeFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _closeFeedbackTimer.Tick += (_, _) => RefreshOperationFeedback();
        Title = title;
        Heading.Text = title;
        EditorHost.Content = editor;
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 32);
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        IsVisibleChanged += (_, _) => { if (!IsVisible) _closeFeedbackTimer.Stop(); };
        Closed += (_, _) =>
        {
            _closeFeedbackTimer.Stop();
            ControlCenterLocalization.Current.LanguageChanged -= RefreshHeading;
        };
        ControlCenterLocalization.Current.LanguageChanged += RefreshHeading;
    }

    public FrameworkElement? DetachEditor()
    {
        var editor = EditorHost.Content as FrameworkElement;
        EditorHost.Content = null;
        return editor;
    }

    public void ShowEditor()
    {
        RefreshOperationFeedback();
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        // Do not steal focus from an input on a duplicate entry click.
        if (!IsKeyboardFocusWithin) DismissButton.Focus();
    }

    public void CloseForOwnerShutdown()
    {
        _ownerShuttingDown = true;
        Close();
    }

    private void RefreshHeading()
    {
        if (_titleKey is null) return;
        Title = ControlCenterLocalization.Current[_titleKey];
        Heading.Text = Title;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_ownerShuttingDown || !_operationInProgress()) return;
        e.Cancel = true;
        SetFooter("settings.dialog.busy");
        _closeFeedbackTimer.Start();
    }

    internal void RefreshOperationFeedback()
    {
        if (_operationInProgress()) return;
        _closeFeedbackTimer.Stop();
        SetFooter("settings.dialog.closeHint");
    }

    private void SetFooter(string key) => FooterText.SetBinding(TextBlock.TextProperty,
        new Binding("[" + key + "]") { Source = ControlCenterLocalization.Current, Mode = BindingMode.OneWay });

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        // Bubbling only: an open picker consumes Escape before its window.
        e.Handled = true;
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
