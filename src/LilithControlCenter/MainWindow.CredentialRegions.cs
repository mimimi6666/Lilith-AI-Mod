using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

public partial class MainWindow
{
    private bool _applyingCredentialCatalog;
    private readonly Dictionary<string, string> _credentialVariantSelections = new(StringComparer.Ordinal);

    private D054HermesCredentialProvider? SelectedCredentialProvider
        => HermesCredentialRegionSelector?.SelectedItem as D054HermesCredentialProvider;

    private void ApplyCredentialFamilies(D054HermesCredentialCatalog catalog, string? preferredProviderId)
    {
        var families = CredentialProviderFamily.From(catalog);
        _applyingCredentialCatalog = true;
        try
        {
            HermesCredentialProviderList.ItemsSource = families;
            HermesCredentialProviderList.SelectedItem = families.FirstOrDefault(family =>
                family.Providers.Any(p => p.ProviderId == preferredProviderId)) ?? families.FirstOrDefault();
            PopulateCredentialVariants(preferredProviderId);
        }
        finally { _applyingCredentialCatalog = false; }
    }

    private void PopulateCredentialVariants(string? preferredProviderId = null)
    {
        var family = HermesCredentialProviderList.SelectedItem as CredentialProviderFamily;
        var wasApplying = _applyingCredentialCatalog;
        _applyingCredentialCatalog = true;
        try
        {
            HermesCredentialRegionSelector.ItemsSource = family?.Providers;
            if (family is null) return;
            preferredProviderId ??= _credentialVariantSelections.GetValueOrDefault(family.Id);
            HermesCredentialRegionSelector.SelectedItem = family.Providers.FirstOrDefault(p => p.ProviderId == preferredProviderId)
                ?? family.Providers.FirstOrDefault();
            if (SelectedCredentialProvider is { } selected)
                _credentialVariantSelections[family.Id] = selected.ProviderId;
        }
        finally { _applyingCredentialCatalog = wasApplying; }
    }

    private void SelectCredentialFamily()
    {
        if (_applyingCredentialCatalog) return;
        ClearCredentialMutationOutcome();
        HermesCredentialApiKeyInput.Clear();
        PopulateCredentialVariants();
        HermesCredentialStatusText.Text = Texts["credential.region.selectionOnly"];
        UpdateHermesCredentialControls();
    }

    private void HermesCredentialRegionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingCredentialCatalog) return;
        ClearCredentialMutationOutcome();
        HermesCredentialApiKeyInput.Clear();
        if (SelectedCredentialProvider is { } selected)
            _credentialVariantSelections[selected.FamilyId] = selected.ProviderId;
        HermesCredentialStatusText.Text = Texts["credential.region.selectionOnly"];
        UpdateHermesCredentialControls();
    }

    private void UpdateCredentialRegionPresentation(bool canPrepare)
    {
        var selected = SelectedCredentialProvider;
        var family = HermesCredentialProviderList.SelectedItem as CredentialProviderFamily;
        HermesCredentialRegionPanel.Visibility = family?.Providers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        HermesCredentialRegionSelector.IsEnabled = canPrepare;
        HermesCredentialEndpointText.Text = selected?.BaseUrl ?? string.Empty;
        HermesCredentialEndpointPanel.Visibility = string.IsNullOrEmpty(selected?.BaseUrl) ? Visibility.Collapsed : Visibility.Visible;
        HermesCredentialRegionStateText.Text = selected is null ? string.Empty
            : Texts[selected.Configured ? "credential.region.keyPresent" : "credential.region.keyAbsent"];
    }
}
