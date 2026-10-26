using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

public partial class ProviderSetupPanel : UserControl
{
    public ProviderSetupEditorState Model { get; } = new();
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? SaveRequested;
    public event RoutedEventHandler? RemoveRequested;
    public event RoutedEventHandler? ModelsRequested;
    public ProviderSetupPanel()
    {
        InitializeComponent();
        SetupContent.DataContext = Model;
        Model.DraftReplaced += ClearSecret;
        Loaded += (_, _) => ControlCenterLocalization.Current.LanguageChanged += Model.RefreshLanguage;
        Unloaded += (_, _) => { ClearSecret(); ControlCenterLocalization.Current.LanguageChanged -= Model.RefreshLanguage; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearSecret(); };
    }
    public string ReadTransientSecret() => SetupApiKey.Password;
    public void ClearSecret() { SetupApiKey.Clear(); Model.HasNewKey = false; }
    private void Key_Changed(object sender, RoutedEventArgs e) => Model.HasNewKey = SetupApiKey.SecurePassword.Length != 0;
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, e);
    private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, e);
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, e);
    private void Models_Click(object sender, RoutedEventArgs e) => ModelsRequested?.Invoke(this, e);
    private void New_Click(object sender, RoutedEventArgs e) => Model.NewEntry();
}
