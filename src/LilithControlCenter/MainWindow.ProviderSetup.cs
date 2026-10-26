using System.IO;
using System.Text.Json;
using System.Windows;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public partial class MainWindow
{
    private bool _providerSetupOperation;
    private bool _providerSetupReading;
    private async void ProviderSetupRefresh_Click(object sender, RoutedEventArgs e) => await RefreshProviderSetupAsync();

    private async Task RefreshProviderSetupAsync(bool background = false)
    {
        if (HermesManagementBusy || _shutdownStarted) return;
        if (ProviderSetupSettingsPanel.Model.HasPendingChanges) return;
        if (background && ProviderSetupSettingsPanel.IsKeyboardFocusWithin) return;
        var preferred = ProviderSetupSettingsPanel.Model.SelectedEntry?.ProviderId;
        var version = ProviderSetupSettingsPanel.Model.DraftVersion;
        D046HermesWorkbenchHost? host = null;
        SetProviderSetupBusy(true, reading: true);
        if (!background) ProviderSetupSettingsPanel.Model.SetStatus("provider.setup.reading");
        try
        {
            host = GetOrCreateHermesWorkbenchHost();
            var catalog = await host.ProviderSetupProjection.ReadProviderSetupAsync(_providerLifetime.Token);
            if (_shutdownStarted || !ReferenceEquals(_hermesWorkbenchHost, host)
                || version != ProviderSetupSettingsPanel.Model.DraftVersion
                || ProviderSetupSettingsPanel.Model.HasPendingChanges) return;
            if (!background || ProviderSetupSettingsPanel.Model.Catalog?.ConfigSha256 != catalog.ConfigSha256)
                ProviderSetupSettingsPanel.Model.Apply(catalog, preferred);
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_shutdownStarted && (host is null || ReferenceEquals(_hermesWorkbenchHost, host))
                && version == ProviderSetupSettingsPanel.Model.DraftVersion
                && !ProviderSetupSettingsPanel.Model.HasPendingChanges)
                ProviderSetupSettingsPanel.Model.Invalidate(ProviderSetupFailureKey(exception));
        }
        finally { SetProviderSetupBusy(false); }
    }

    private async void ProviderSetupModels_Click(object sender, RoutedEventArgs e)
    {
        if (HermesManagementBusy || !ProviderSetupSettingsPanel.Model.CanReadModels) return;
        var request = ProviderSetupSettingsPanel.Model.CreateModelsRequest();
        var version = ProviderSetupSettingsPanel.Model.DraftVersion;
        D046HermesWorkbenchHost? host = null;
        SetProviderSetupBusy(true, reading: true);
        ProviderSetupSettingsPanel.Model.SetStatus("provider.setup.readingModels");
        try
        {
            // Only a saved exact entry is queried, and only from this explicit click.
            host = GetOrCreateHermesWorkbenchHost();
            var models = await host.ProviderSetupProjection.ReadProviderModelsAsync(
                request.GetProperty("provider_id").GetString()!, request.GetProperty("expected_config_sha256").GetString()!,
                _providerLifetime.Token);
            if (_shutdownStarted || !ReferenceEquals(_hermesWorkbenchHost, host)
                || version != ProviderSetupSettingsPanel.Model.DraftVersion) return;
            ProviderSetupSettingsPanel.Model.ApplyModels(models);
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_shutdownStarted && ReferenceEquals(_hermesWorkbenchHost, host)
                && version == ProviderSetupSettingsPanel.Model.DraftVersion)
                ProviderSetupSettingsPanel.Model.SetStatus(ProviderSetupFailureKey(exception));
        }
        finally { SetProviderSetupBusy(false); }
    }

    private async void ProviderSetupSave_Click(object sender, RoutedEventArgs e)
    {
        if (HermesManagementBusy || !ProviderSetupSettingsPanel.Model.CanSave) return;
        if (!AdmitSettingsHostRetirement(SettingsMutationSurface.CustomProvider)) return;
        JsonElement request;
        var secret = ProviderSetupSettingsPanel.ReadTransientSecret();
        try { request = ProviderSetupSettingsPanel.Model.CreateSaveRequest(secret, "cc-" + Guid.NewGuid().ToString("N")); }
        catch (Exception exception) when (exception is InvalidDataException or ProviderSetupOperationException)
        {
            ProviderSetupSettingsPanel.Model.SetStatus(ProviderSetupFailureKey(exception));
            return;
        }
        finally { secret = string.Empty; ProviderSetupSettingsPanel.ClearSecret(); }
        await MutateProviderSetupAsync(HermesProviderSetupContract.SaveMethod, request);
    }

    private async void ProviderSetupRemove_Click(object sender, RoutedEventArgs e)
    {
        if (HermesManagementBusy || !ProviderSetupSettingsPanel.Model.CanRemove) return;
        if (!AdmitSettingsHostRetirement(SettingsMutationSurface.CustomProvider)) return;
        var selected = ProviderSetupSettingsPanel.Model.SelectedEntry!;
        var catalog = ProviderSetupSettingsPanel.Model.Catalog!;
        var version = ProviderSetupSettingsPanel.Model.DraftVersion;
        var confirmationHost = _hermesWorkbenchHost;
        var confirmationGeneration = confirmationHost?.RuntimeGeneration;
        var mutationStarted = false;
        try
        {
            var request = ProviderSetupSettingsPanel.Model.CreateRemoveRequest("cc-" + Guid.NewGuid().ToString("N"));
            // ShowDialog pumps a nested dispatcher. Reserve the shared operation
            // owner before opening it, then bind confirmation to this exact view.
            SetProviderSetupBusy(true);
            var confirmation = new ThemedConfirmationDialog(Texts["provider.setup.remove"],
                Texts.Format("provider.setup.removeConfirm", selected.Label), Texts["provider.setup.remove"]) { Owner = this };
            TraceProviderSetup("confirmation", "opened");
            if (confirmation.ShowDialog() != true)
            {
                ProviderSetupSettingsPanel.Model.SetStatus("provider.setup.cancelled");
                TraceProviderSetup("confirmation", "cancelled");
                return;
            }
            // This operation owns busy; do not reject our own reservation.
            if (_shutdownStarted || !ReferenceEquals(confirmationHost, _hermesWorkbenchHost)
                || confirmationGeneration != _hermesWorkbenchHost?.RuntimeGeneration
                || _hermesWorkbenchHost is { RequiresProjectionRecovery: true }
                || !ProviderSetupSettingsPanel.Model.IsRemovalConfirmationCurrent(catalog, selected, version))
            {
                ProviderSetupSettingsPanel.Model.SetStatus("provider.setup.notSent");
                TraceProviderSetup("confirmation", "not_sent");
                return;
            }
            if (!AdmitSettingsHostRetirement(SettingsMutationSurface.CustomProvider)) return;
            mutationStarted = true;
            await MutateProviderSetupAsync(HermesProviderSetupContract.RemoveMethod, request);
        }
        catch (Exception)
        {
            if (!_shutdownStarted) ProviderSetupSettingsPanel.Model.SetStatus(
                mutationStarted ? "provider.setup.unknown" : "provider.setup.notSent");
            TraceProviderSetup(mutationStarted ? "completion" : "confirmation",
                mutationStarted ? "outcome_unknown" : "not_sent");
        }
        finally { ProviderSetupSettingsPanel.ClearSecret(); SetProviderSetupBusy(false); }
    }

    private async Task MutateProviderSetupAsync(string method, JsonElement request)
    {
        var previousCatalog = ProviderSetupSettingsPanel.Model.Catalog
            ?? throw new InvalidOperationException("Provider setup is not current.");
        var preferredCredentialProvider = SelectedCredentialProvider?.ProviderId;
        SetProviderSetupBusy(true);
        ProviderSetupSettingsPanel.Model.SetStatus("provider.setup.saving");
        var authoritativeReadback = false;
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            var oldGeneration = host.RuntimeGeneration;
            HermesProviderSetupMutation? receipt = null;
            ProviderSetupOperationException? knownFailure = null;
            try
            {
                TraceProviderSetup("dispatch", "started");
                receipt = await host.ProviderSetupProjection.MutateProviderSetupAsync(method, request, _providerLifetime.Token);
                TraceProviderSetup("dispatch", "receipt_received");
            }
            catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested) { return; }
            catch (ProviderSetupOperationException exception)
            {
                knownFailure = exception;
                TraceProviderSetup("dispatch", exception.MutationDispatched == false ? "not_sent" : "rejected");
            }
            catch (Exception) { TraceProviderSetup("dispatch", "outcome_unknown"); }

            if (knownFailure?.MutationDispatched == false)
            {
                // Only the closed Runtime failure can prove no native dispatch.
                // Re-read metadata once, without replaying the rejected write.
                ProviderSetupSettingsPanel.Model.Invalidate();
                var current = await host.ProviderSetupProjection.ReadProviderSetupAsync(_providerLifetime.Token);
                authoritativeReadback = true;
                ProviderSetupSettingsPanel.Model.Apply(current, request.GetProperty("provider_id").GetString());
                ProviderSetupSettingsPanel.Model.SetStatus(ProviderSetupFailureKey(knownFailure));
                TraceProviderSetup("readback", "current_not_sent");
                return;
            }

            InvalidateHermesCredentialCatalog();
            ProviderSetupSettingsPanel.Model.Invalidate();
            var catalog = await ReconnectProviderSetupAsync(host, oldGeneration,
                preferredCredentialProvider, _providerLifetime.Token);
            authoritativeReadback = true;
            TraceProviderSetup("readback", "fresh_catalog");
            if (knownFailure is not null)
            {
                ProviderSetupSettingsPanel.Model.Apply(catalog, request.GetProperty("provider_id").GetString());
                ProviderSetupSettingsPanel.Model.SetStatus(ProviderSetupFailureKey(knownFailure));
                return;
            }
            var verified = ProviderSetupSettingsPanel.Model.CompleteMutationReadback(
                catalog, receipt, method, request, previousCatalog);
            TraceProviderSetup("completion", verified ? "verified" : "outcome_unknown");
            // No model discovery/default setter/session replay follows this mutation.
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (authoritativeReadback) ProviderSetupSettingsPanel.Model.SetStatus("provider.setup.unknown");
            else ProviderSetupSettingsPanel.Model.Invalidate("provider.setup.unknown");
            TraceProviderSetup(authoritativeReadback ? "completion" : "readback", "outcome_unknown");
        }
        finally { ProviderSetupSettingsPanel.ClearSecret(); SetProviderSetupBusy(false); }
    }

    private async Task<HermesProviderSetupCatalog> ReconnectProviderSetupAsync(D046HermesWorkbenchHost staleHost,
        Guid staleGeneration, string? preferredCredentialProvider, CancellationToken cancellationToken)
    {
        await RetireHermesWorkbenchHostAsync(staleHost);
        using var reconnect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reconnect.CancelAfter(HermesCredentialReconnectTimeout);
        while (true)
        {
            reconnect.Token.ThrowIfCancellationRequested();
            var candidate = GetOrCreateHermesWorkbenchHost();
            try
            {
                var catalog = await candidate.ProviderSetupProjection.ReadProviderSetupAsync(reconnect.Token);
                var generation = candidate.RuntimeGeneration;
                D054HermesCredentialGenerationAdmission.RequireFresh(staleGeneration, generation);
                var credentials = await candidate.GetCredentialStatusAsync(reconnect.Token);
                if (!ReferenceEquals(candidate, _hermesWorkbenchHost) || candidate.RuntimeGeneration != generation)
                    throw new InvalidDataException("Provider metadata belongs to a retired generation.");
                ApplyHermesCredentialCatalog(credentials, preferredCredentialProvider);
                return catalog;
            }
            catch (OperationCanceledException) { await RetireHermesWorkbenchHostAsync(candidate); throw; }
            catch (Exception) { await RetireHermesWorkbenchHostAsync(candidate); }
            await Task.Delay(HermesCredentialReconnectRetryDelay, reconnect.Token);
        }
    }

    private void SetProviderSetupBusy(bool busy, bool reading = false)
    {
        _providerSetupOperation = busy;
        _providerSetupReading = busy && reading;
        UpdateHermesManagementControls();
    }
    private void UpdateProviderSetupInteractionState()
    {
        var busy = HermesManagementPolicy.MutationOrRecoveryInProgress || _shutdownStarted;
        ProviderSetupSettingsPanel.Model.Busy = busy;
        ProviderSetupSettingsPanel.Model.Reading = HermesManagementBusy && !busy;
    }
    private static void TraceProviderSetup(string stage, string outcome)
    {
        // All arguments are closed call-site constants. Never include requests,
        // labels, endpoints, keys, paths or exception text in this diagnostic.
        try { App.TraceStartup($"ProviderSetup stage={stage} outcome={outcome}"); }
        catch (Exception) { }
    }
    private static string ProviderSetupFailureKey(Exception exception) => exception switch
    {
        ProviderSetupOperationException { Code: "setup.unsupported" } => "provider.setup.unsupported",
        ProviderSetupOperationException { Code: "setup.key_required" } => "provider.setup.keyRequired",
        ProviderSetupOperationException { Code: "setup.endpoint_changed_requires_key" } => "provider.setup.endpointKey",
        ProviderSetupOperationException { Code: "setup.busy" } => "provider.setup.busy",
        ProviderSetupOperationException { Code: "setup.entry_in_use" } => "provider.setup.inUse",
        ProviderSetupOperationException { Code: "setup.stale_config" } => "provider.setup.stale",
        ProviderSetupOperationException { Code: "setup.limit_reached" } => "provider.setup.limit",
        ProviderSetupOperationException { Code: "setup.not_found" } => "provider.setup.notFound",
        ProviderSetupOperationException { Code: "setup.invalid_request" } => "provider.setup.invalid",
        ProviderSetupOperationException { Code: "setup.cancelled" } => "provider.setup.cancelled",
        ProviderSetupOperationException { Code: "setup.request_not_sent" } => "provider.setup.notSent",
        ProviderSetupOperationException { Code: "setup.unauthorized", MutationDispatched: false } => "provider.setup.notSent",
        InvalidDataException => "provider.setup.invalid",
        _ => "provider.setup.unknown"
    };
}
