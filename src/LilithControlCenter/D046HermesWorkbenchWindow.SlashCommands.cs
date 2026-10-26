using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace LilithControlCenter;

public partial class D046HermesWorkbenchWindow
{
    private Popup? _slashPopup;
    private ListBox? _slashList;
    private TextBlock? _slashHeading;
    private TextBlock? _slashHint;
    private TextBlock? _slashStatus;
    private D046HermesWorkbenchViewModel? _slashOwner;
    private bool _slashReadAttempted;
    private bool _slashReadPending;
    private string? _slashDismissedText;

    private sealed record SlashMenuRow(D046HermesSlashSuggestion Candidate, string Description)
    {
        public string Command => Candidate.Command;
    }

    private void InitializeSlashCommands()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        _slashHeading = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 6) };
        _slashHeading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(_slashHeading);
        _slashStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 6) };
        _slashStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(_slashStatus);
        _slashList = new ListBox
        {
            MaxHeight = 240, BorderThickness = new Thickness(0), Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _slashList.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        _slashList.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var rowStyle = new Style(typeof(ListBoxItem), TryFindResource(typeof(ListBoxItem)) as Style);
        rowStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        _slashList.ItemContainerStyle = rowStyle;
        AutomationProperties.SetAutomationId(_slashList, "HermesSlashCommandList");
        var rowPanel = new FrameworkElementFactory(typeof(StackPanel));
        rowPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 4, 5, 4));
        var command = new FrameworkElementFactory(typeof(TextBlock));
        command.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        command.SetBinding(TextBlock.TextProperty, new Binding(nameof(SlashMenuRow.Command)));
        rowPanel.AppendChild(command);
        var description = new FrameworkElementFactory(typeof(TextBlock));
        description.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        description.SetValue(TextBlock.FontSizeProperty, 12d);
        description.SetBinding(TextBlock.TextProperty, new Binding(nameof(SlashMenuRow.Description)));
        rowPanel.AppendChild(description);
        _slashList.ItemTemplate = new DataTemplate { VisualTree = rowPanel };
        _slashList.PreviewMouseLeftButtonUp += SlashList_MouseLeftButtonUp;
        panel.Children.Add(_slashList);
        _slashHint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(4, 8, 4, 0) };
        _slashHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(_slashHint);
        var border = new Border
        {
            Child = panel, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10)
        };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _slashPopup = new Popup
        {
            Child = border, PlacementTarget = HermesMessageInput, Placement = PlacementMode.Top,
            StaysOpen = true, AllowsTransparency = true, Focusable = false,
            VerticalOffset = -6
        };
        HermesMessageInput.TextChanged += SlashInput_TextChanged;
        HermesMessageInput.SelectionChanged += SlashInput_SelectionChanged;
        HermesMessageInput.PreviewKeyDown += SlashInput_PreviewKeyDown;
        HermesMessageInput.GotKeyboardFocus += SlashInput_GotKeyboardFocus;
        HermesMessageInput.LostKeyboardFocus += SlashInput_LostKeyboardFocus;
        DataContextChanged += SlashWindow_DataContextChanged;
        Deactivated += SlashWindow_Deactivated;
        ControlCenterLocalization.Current.LanguageChanged += RefreshSlashPopup;
        BindSlashOwner(DataContext as D046HermesWorkbenchViewModel);
    }

    private void DisposeSlashCommands()
    {
        if (_slashPopup is not null) _slashPopup.IsOpen = false;
        BindSlashOwner(null);
        HermesMessageInput.TextChanged -= SlashInput_TextChanged;
        HermesMessageInput.SelectionChanged -= SlashInput_SelectionChanged;
        HermesMessageInput.PreviewKeyDown -= SlashInput_PreviewKeyDown;
        HermesMessageInput.GotKeyboardFocus -= SlashInput_GotKeyboardFocus;
        HermesMessageInput.LostKeyboardFocus -= SlashInput_LostKeyboardFocus;
        if (_slashList is not null) _slashList.PreviewMouseLeftButtonUp -= SlashList_MouseLeftButtonUp;
        DataContextChanged -= SlashWindow_DataContextChanged;
        Deactivated -= SlashWindow_Deactivated;
        ControlCenterLocalization.Current.LanguageChanged -= RefreshSlashPopup;
    }

    private void BindSlashOwner(D046HermesWorkbenchViewModel? owner)
    {
        if (_slashOwner is not null) _slashOwner.PropertyChanged -= SlashOwner_PropertyChanged;
        _slashOwner = owner;
        _slashReadAttempted = false;
        _slashReadPending = false;
        _slashDismissedText = null;
        if (owner is not null) owner.PropertyChanged += SlashOwner_PropertyChanged;
        RefreshSlashPopup();
    }

    private void SlashWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => BindSlashOwner(e.NewValue as D046HermesWorkbenchViewModel);
    private void SlashWindow_Deactivated(object? sender, EventArgs e) { if (_slashPopup is not null) _slashPopup.IsOpen = false; }
    private void SlashOwner_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _slashOwner)) RefreshSlashPopup();
    }
    private void SlashInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        _slashDismissedText = null;
        RefreshSlashPopup();
    }
    private void SlashInput_SelectionChanged(object sender, RoutedEventArgs e) => RefreshSlashPopup();
    private void SlashInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshSlashPopup();
    private void SlashInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_slashPopup is not null && !_slashPopup.IsKeyboardFocusWithin) _slashPopup.IsOpen = false;
    }

    private void RefreshSlashPopup()
    {
        if (_slashPopup is null || _slashList is null || _slashHeading is null || _slashHint is null || _slashStatus is null)
            return;
        if (_connectionWindowClosed || _connectionRecoveryPending || _slashOwner is not { CanBrowseComposerCommands: true }
            || !IsActive || !HermesMessageInput.IsKeyboardFocused
            || HermesMessageInput.Text == _slashDismissedText
            || !D046HermesSlashCommands.TryGetQuery(HermesMessageInput.Text, HermesMessageInput.CaretIndex,
                HermesMessageInput.SelectionLength, out var query))
        {
            _slashPopup.IsOpen = false;
            if (!HermesMessageInput.Text.StartsWith("/", StringComparison.Ordinal)) _slashReadAttempted = false;
            return;
        }
        var texts = ControlCenterLocalization.Current;
        _slashHeading.Text = texts["chat.slash.title"];
        _slashHint.Text = texts["chat.slash.hint"];
        var selected = (_slashList.SelectedItem as SlashMenuRow)?.Command;
        var candidates = D046HermesSlashCommands.Filter(_slashOwner.ComposerCommandSuggestions, query);
        var rows = candidates.Select(candidate => new SlashMenuRow(candidate, texts[candidate.DescriptionKey])).ToArray();
        _slashList.ItemsSource = rows;
        _slashList.SelectedIndex = rows.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(rows, row => row.Command == selected));
        _slashList.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _slashStatus.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _slashStatus.Text = texts[_slashOwner.HasCurrentComposerCommandCatalog ? "chat.slash.empty"
            : !_slashReadAttempted || _slashReadPending ? "chat.slash.loading" : "chat.slash.unavailable"];
        _slashPopup.Width = Math.Max(220, HermesMessageInput.ActualWidth);
        _slashPopup.IsOpen = true;
        if (!_slashOwner.HasCurrentComposerCommandCatalog && !_slashReadAttempted)
            ReadSlashCatalogAsync(_slashOwner);
    }

    private async void ReadSlashCatalogAsync(D046HermesWorkbenchViewModel owner)
    {
        _slashReadAttempted = true;
        _slashReadPending = true;
        // Read-only, single-flight catalog cache. No per-keypress RPC and no
        // waiting on the UI thread; completion can never execute a command.
        try { await owner.EnsureComposerCommandCatalogLoadedAsync(); }
        catch (Exception) { /* A typed unavailable hint is rendered below. */ }
        finally
        {
            if (ReferenceEquals(owner, _slashOwner) && !_connectionWindowClosed)
            {
                _slashReadPending = false;
                RefreshSlashPopup();
            }
        }
    }

    private void SlashInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_slashPopup?.IsOpen != true || _slashList is null || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape)
        {
            _slashDismissedText = HermesMessageInput.Text;
            _slashPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            _slashList.SelectedIndex = D046HermesSlashCommands.MoveSelection(_slashList.SelectedIndex,
                _slashList.Items.Count, e.Key == Key.Down ? 1 : -1);
            if (_slashList.SelectedItem is { } selected) _slashList.ScrollIntoView(selected);
            e.Handled = true;
        }
        else if ((e.Key is Key.Tab or Key.Enter) && _slashList.SelectedItem is SlashMenuRow row)
        {
            InsertSlashSuggestion(row);
            e.Handled = true;
        }
    }

    private void SlashList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_slashList is null || e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(_slashList, source) is not ListBoxItem { Content: SlashMenuRow row }) return;
        InsertSlashSuggestion(row);
        e.Handled = true;
    }

    private void InsertSlashSuggestion(SlashMenuRow row)
    {
        if (_slashOwner is null || !D046HermesSlashCommands.TryGetQuery(HermesMessageInput.Text,
                HermesMessageInput.CaretIndex, HermesMessageInput.SelectionLength, out _)
            || !_slashOwner.ComposerCommandSuggestions.Any(candidate => candidate == row.Candidate)) return;
        // Insertion only. The normal Send / Ctrl+Enter route is still required.
        _slashOwner.DraftText = row.Command + " ";
        HermesMessageInput.Focus();
        HermesMessageInput.CaretIndex = HermesMessageInput.Text.Length;
        _slashPopup!.IsOpen = false;
    }
}
