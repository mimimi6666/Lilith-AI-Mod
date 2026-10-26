using System.Windows;

namespace LilithControlCenter;

public partial class MainWindow
{
    private readonly CredentialMutationPresentationState _credentialMutationPresentation = new();

    private void ClearCredentialMutationOutcome() => _credentialMutationPresentation.Clear();

    private void ShowCredentialObservationStatus(string message, bool background, bool failed = false)
        => HermesCredentialStatusText.Text = _credentialMutationPresentation.Observe(
            SelectedCredentialProvider?.ProviderId, message, background, failed);

    private void ShowCredentialMutationOutcome(string providerId, string message)
    {
        _credentialMutationPresentation.Remember(providerId, message);
        HermesCredentialStatusText.Text = message;
    }

    private async Task DeleteHermesCredentialAsync()
    {
        var selected = SelectedCredentialProvider;
        if (HermesManagementBusy || _shutdownStarted || !_hermesCredentialCatalogAuthoritative
            || selected is not { Configured: true })
        {
            if (!_shutdownStarted)
                HermesCredentialStatusText.Text = Texts["credential.delete.notSent"];
            TraceCredentialDeletion(selected?.ProviderId, "admission", "not_sent");
            return;
        }
        if (!AdmitSettingsHostRetirement(SettingsMutationSurface.Credentials))
        {
            TraceCredentialDeletion(selected.ProviderId, "admission", "draft_blocked");
            return;
        }

        var confirmationHost = _hermesWorkbenchHost;
        var confirmationGeneration = confirmationHost?.RuntimeGeneration;
        var dispatched = false;
        ClearCredentialMutationOutcome();
        try
        {
            // ShowDialog pumps a nested dispatcher. Reserve the existing shared
            // mutation owner BEFORE opening it so periodic reads cannot take the
            // busy flag, replace this selection or silently consume confirmation.
            SetHermesCredentialBusy(true);
            var confirmation = new ThemedConfirmationDialog(
                Texts["credential.delete.title"],
                Texts.Format("credential.delete.confirmBody", selected.DisplayName),
                Texts["credential.delete.confirmButton"])
            {
                Owner = this
            };
            TraceCredentialDeletion(selected.ProviderId, "confirmation", "opened");
            if (confirmation.ShowDialog() != true)
            {
                if (!_shutdownStarted)
                    ShowCredentialMutationOutcome(selected.ProviderId, Texts["credential.delete.cancelled"]);
                TraceCredentialDeletion(selected.ProviderId, "confirmation", "cancelled");
                return;
            }

            // Do not test HermesManagementBusy here: this operation owns it.
            // The explicit confirmation is bound to exactly the original row
            // and host; a changed session/selection needs a new human decision.
            var current = SelectedCredentialProvider;
            if (!CredentialMutationPresentationState.CanDispatchDeletion(
                confirmed: true,
                shuttingDown: _shutdownStarted,
                catalogCurrent: _hermesCredentialCatalogAuthoritative,
                selectedProviderMatches: current is { Configured: true }
                    && string.Equals(current.ProviderId, selected.ProviderId, StringComparison.Ordinal),
                hostCurrent: ReferenceEquals(confirmationHost, _hermesWorkbenchHost)
                    && confirmationGeneration == _hermesWorkbenchHost?.RuntimeGeneration
                    && _hermesWorkbenchHost is not { RequiresProjectionRecovery: true }))
            {
                if (!_shutdownStarted)
                    ShowCredentialMutationOutcome(selected.ProviderId, Texts["credential.delete.notSent"]);
                TraceCredentialDeletion(selected.ProviderId, "confirmation", "not_sent");
                return;
            }
            if (!AdmitSettingsHostRetirement(SettingsMutationSurface.Credentials))
            {
                ShowCredentialMutationOutcome(selected.ProviderId, HermesCredentialStatusText.Text);
                TraceCredentialDeletion(selected.ProviderId, "confirmation", "draft_blocked");
                return;
            }

            HermesCredentialStatusText.Text = Texts.Format("credential.delete.progress", selected.DisplayName);
            var host = GetOrCreateHermesWorkbenchHost();
            var oldGeneration = host.RuntimeGeneration;
            D054HermesCredentialMutationReceipt? receipt = null;
            Exception? mutationFailure = null;
            try
            {
                dispatched = true;
                TraceCredentialDeletion(selected.ProviderId, "dispatch", "started");
                receipt = await host.RemoveCredentialAsync(selected.ProviderId, _providerLifetime.Token);
                TraceCredentialDeletion(selected.ProviderId, "dispatch", "receipt_received");
            }
            catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested)
            {
                TraceCredentialDeletion(selected.ProviderId, "dispatch", "cancelled_unknown");
                return;
            }
            catch (ArgumentException exception)
            {
                ShowCredentialMutationOutcome(selected.ProviderId,
                    HermesCredentialMutationFailureText(exception, Texts["ui.delete"]));
                TraceCredentialDeletion(selected.ProviderId, "dispatch", "argument_rejected");
                return;
            }
            catch (Exception exception)
            {
                mutationFailure = exception;
                TraceCredentialDeletion(selected.ProviderId, "dispatch",
                    exception is D054HermesCredentialOperationException ? "rejected" : "outcome_unknown");
            }

            InvalidateHermesCredentialCatalog();
            Exception? reconnectFailure = null;
            try
            {
                await ReconnectHermesCredentialHostAsync(host, oldGeneration, selected.ProviderId,
                    expectedConfigured: false, _providerLifetime.Token);
                TraceCredentialDeletion(selected.ProviderId, "readback", "unconfigured_verified");
            }
            catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested)
            {
                TraceCredentialDeletion(selected.ProviderId, "readback", "cancelled_unknown");
                return;
            }
            catch (Exception exception)
            {
                reconnectFailure = exception;
                TraceCredentialDeletion(selected.ProviderId, "readback", "unconfirmed");
            }

            if (receipt is null)
            {
                var failure = mutationFailure ?? new InvalidOperationException("Credential mutation returned no receipt.");
                var message = HermesCredentialMutationFailureText(failure, Texts["ui.delete"]);
                message += reconnectFailure is null
                    ? Texts["state.reconnected.check.the.list.above.for.the.actual.hermes.state.405"]
                    : Texts["state.lilith.has.not.finished.restarting.refresh.again.shortly.406"];
                ShowCredentialMutationOutcome(selected.ProviderId, message);
                return;
            }
            if (reconnectFailure is not null)
            {
                ShowCredentialMutationOutcome(selected.ProviderId,
                    Texts.Format("credential.delete.reconnectUncertain", receipt.DisplayName));
                return;
            }
            ShowCredentialMutationOutcome(selected.ProviderId,
                Texts.Format("credential.delete.completed", receipt.DisplayName)
                + " " + Texts["credential.delete.removedProviderRemains"]);
            TraceCredentialDeletion(selected.ProviderId, "completion", "verified");
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (dispatched) InvalidateHermesCredentialCatalog();
            if (!_shutdownStarted)
                ShowCredentialMutationOutcome(selected.ProviderId,
                    Texts[dispatched ? "credential.delete.outcomeUnknown" : "credential.delete.notSent"]);
            TraceCredentialDeletion(selected.ProviderId, "completion", dispatched ? "outcome_unknown" : "not_sent");
        }
        finally
        {
            HermesCredentialApiKeyInput.Clear();
            SetHermesCredentialBusy(false);
        }
    }

    private static void TraceCredentialDeletion(string? providerId, string stage, string outcome)
    {
        // Inputs are closed call-site labels and a validated non-secret provider
        // identity, never keys, payloads, paths or exception prose. Logging must
        // not change the result of an already-dispatched operation.
        var safeProvider = providerId is { Length: > 0 and <= 128 }
            && providerId.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
                ? providerId : "unknown";
        try { App.TraceStartup($"CredentialDelete provider={safeProvider} stage={stage} outcome={outcome}"); }
        catch (Exception) { }
    }
}
