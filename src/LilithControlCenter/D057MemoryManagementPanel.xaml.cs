using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LilithControlCenter;

public partial class D057MemoryManagementPanel : UserControl
{
    private readonly D057MemoryManagementViewModel _viewModel = new();
    private readonly CancellationTokenSource _lifetime = new();

    public D057MemoryManagementPanel()
    {
        InitializeComponent();
        // MainWindow replaces its root DataContext once per telemetry refresh.
        // This panel intentionally owns a stable, independent view model.
        DataContext = _viewModel;
        Loaded += (_, _) =>
        {
            ControlCenterLocalization.Current.LanguageChanged -= RefreshLanguage;
            ControlCenterLocalization.Current.LanguageChanged += RefreshLanguage;
            RefreshLanguage();
        };
        Unloaded += (_, _) => ControlCenterLocalization.Current.LanguageChanged -= RefreshLanguage;
    }

    private void RefreshLanguage()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshLanguage); return; }
        _viewModel.RefreshLanguage();
    }

    internal bool CanRetireMemoryProjection => !_viewModel.IsBusy
        && !_viewModel.IsEditorOpen && !_viewModel.IsPermanentDeleteOpen;

    internal void AttachMemoryProjection(
        ID057MemoryManagementProjection projection)
        => _viewModel.AttachProjection(projection);

    internal void DetachMemoryProjection()
        => _viewModel.DetachProjection();

    internal Task RefreshMemoryAsync()
        => _viewModel.RefreshAsync(_lifetime.Token);

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshAsync(_lifetime.Token);

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button
            {
                DataContext: D057MemoryCategoryViewModel category
            })
        {
            _viewModel.SelectCategory(category.Category);
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BeginAdd();
        FocusEditor();
    }

    private void CorrectButton_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is not { } entry)
            return;
        _viewModel.BeginCorrection(entry);
        FocusEditor();
    }

    private void CancelEditorButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.CancelEditor();

    private async void CommitEditorButton_Click(
        object sender,
        RoutedEventArgs e)
        => await _viewModel.CommitEditorAsync(_lifetime.Token);

    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is { } entry)
            await _viewModel.TogglePinAsync(entry, _lifetime.Token);
    }

    private async void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is { } entry)
            await _viewModel.AcceptCandidateAsync(entry, _lifetime.Token);
    }

    private async void RejectButton_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is { } entry)
            await _viewModel.RejectCandidateAsync(entry, _lifetime.Token);
    }

    private async void PromoteTemporaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is { } entry)
            await _viewModel.PromoteTemporaryAsync(entry, _lifetime.Token);
    }

    private async void VisibilityButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is { } entry)
            await _viewModel.ToggleVisibilityAsync(entry, _lifetime.Token);
    }

    private void PermanentDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFromSender(sender) is { } entry)
            _viewModel.BeginPermanentDelete(entry);
    }

    private void CancelPermanentDeleteButton_Click(
        object sender,
        RoutedEventArgs e)
        => _viewModel.CancelPermanentDelete();

    private async void ConfirmPermanentDeleteButton_Click(
        object sender,
        RoutedEventArgs e)
        => await _viewModel.ConfirmPermanentDeleteAsync(_lifetime.Token);

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
        => await _viewModel.ExportAsync(_lifetime.Token);

    private async void AutoMemoryToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsChecked: bool enabled })
            await _viewModel.SetAutoMemoryEnabledAsync(enabled, _lifetime.Token);
    }

    private void FocusEditor()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                D057MemoryEditorInput.Focus();
                D057MemoryEditorInput.CaretIndex =
                    D057MemoryEditorInput.Text.Length;
            }));
    }

    private static D057MemoryEntryViewModel? EntryFromSender(object sender)
        => sender is Button
        {
            DataContext: D057MemoryEntryViewModel entry
        }
            ? entry
            : null;

    private void D057MemoryManagementPanel_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is null)
            _lifetime.Cancel();
    }
}
