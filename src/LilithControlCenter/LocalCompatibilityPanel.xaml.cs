using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

public partial class LocalCompatibilityPanel : UserControl
{
    internal LocalCompatibilityViewModel Model { get; } = new();
    public LocalCompatibilityPanel()
    {
        InitializeComponent(); ContentRoot.DataContext = Model;
        Loaded += (_, _) => ControlCenterLocalization.Current.LanguageChanged += Model.RefreshLanguage;
        Unloaded += (_, _) => ControlCenterLocalization.Current.LanguageChanged -= Model.RefreshLanguage;
    }
    public void Attach(ILocalCompatibilityProjection projection) => Model.Attach(projection);
    public void Detach() => Model.Detach();
    public Task RefreshAsync() => Model.RefreshAsync();
    // There is no free-form draft in this card. Keeping keyboard focus on its
    // switch or refresh button must not prevent a read-only uncertain recovery.
    internal Task ObserveAsync() => Model.IsBusy || Model.IsDirty
        ? Task.CompletedTask : Model.RefreshAsync(background: true);
    private async void Toggle_Click(object sender, RoutedEventArgs e) => await Model.SaveAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
}
