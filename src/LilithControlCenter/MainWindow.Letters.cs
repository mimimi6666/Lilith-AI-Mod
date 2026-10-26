using System.Text.Json;
using System.Windows;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed record D059LettersStatus(bool Enabled, int DailyLimit, string Reason, int DeliveredToday);
public interface ID059LettersProjection
{
    Task<D059LettersStatus> LettersSettingsAsync(bool? enabled, int dailyLimit, CancellationToken cancellationToken);
}

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : ID059LettersProjection
{
    public async Task<D059LettersStatus> LettersSettingsAsync(bool? enabled, int dailyLimit, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var generation = RuntimeGeneration;
        var method = enabled.HasValue ? "runtime.letters.set" : "runtime.letters.get";
        var data = enabled.HasValue ? JsonSerializer.SerializeToElement(new { enabled = enabled.Value, daily_limit = dailyLimit }) : JsonSerializer.SerializeToElement(new { });
        return await SendRequestAsync(new(method, data), (response, context) =>
        {
            if (!response.Succeeded || context.GenerationId != generation) throw new InvalidOperationException("Letters settings unavailable.");
            var value = response.HermesPayload;
            LilithNoteInboxProtocol.Exact(value, "enabled", "daily_limit", "revision", "reason", "pending", "delivered_today");
            var settings = new LilithLettersSettings(value.GetProperty("enabled").GetBoolean(), value.GetProperty("daily_limit").GetInt32());
            return new D059LettersStatus(settings.Enabled, settings.DailyLimit, value.GetProperty("reason").GetString()!, value.GetProperty("delivered_today").GetInt32());
        }, cancellationToken, responseTimeout: TimeSpan.FromSeconds(5), isDecodeFailureIsolated: true).ConfigureAwait(false);
    }
}

public partial class MainWindow
{
    private bool _lettersSettingsBusy;
    private int _lettersConfirmedLimit = 1;
    private D059LettersStatus? _lettersDisplayStatus;
    private string? _lettersDisplayError;
    private void ShowLettersError(string key)
    { _lettersDisplayStatus = null; _lettersDisplayError = key; RenderLettersStatus(); }
    private void RenderLettersStatus()
    {
        if (LettersStatusText is null) return;
        if (_lettersDisplayError is { } error) { LettersStatusText.Text = Texts[error]; return; }
        if (_lettersDisplayStatus is not { } status) return;
        var parts = status.Reason.Split('|');
        var operatorTest = parts[0] == "operator_test";
        var test = operatorTest || parts[0] == "test_timing";
        var reason = parts.Length > (test ? 1 : 0) ? parts[test ? 1 : 0] : "state_unavailable";
        var detail = parts.Length > (test ? 2 : 1) ? parts[^1] : null;
        var key = reason switch
        {
            "preparing" or "writing" or "delivered" or "daily_limit_reached" or "state_unavailable"
                or "writing_unavailable" or "no_suitable_letter" or "no_current_material"
                or "evaluation_backoff" or "preparation_backoff" or "evaluation_cooldown" or "evaluation_limit_reached"
                or "waiting_for_conversation" or "accumulating_conversation"
                or "awaiting_safe_absence" => reason,
            "delivery_unresolved" or "legacy_delivery_unresolved" => "delivery_unresolved",
            _ => "waiting"
        };
        LettersStatusText.Text = (status.Enabled ? Texts.Format("letters.enabledCount", status.DeliveredToday) + " " + Texts["letters.reason." + key] : Texts["letters.disabled"])
            + (operatorTest ? Texts["letters.operatorTest"] : test ? Texts["letters.shortTest"] : "")
            + (detail is null ? "" : Texts["letters.diagnostic"] + detail);
    }
    private async void LettersRefresh_Click(object sender, RoutedEventArgs e) => await UpdateLettersSettingsAsync(false);
    private async void LettersSave_Click(object sender, RoutedEventArgs e) => await UpdateLettersSettingsAsync(true);
    private async void LettersPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (e.NewValue is true) await UpdateLettersSettingsAsync(false); }

    private async Task UpdateLettersSettingsAsync(bool save, bool background = false)
    {
        if (_lettersSettingsBusy || LettersStatusText is null) return;
        if (background && (LettersDailyLimit.IsKeyboardFocusWithin
            || LettersDailyLimit.Text != _lettersConfirmedLimit.ToString())) return;
        if (_dailyApprovalProjection is not ID059LettersProjection projection)
        { ShowLettersError("letters.notReady"); return; }
        var limit = _lettersConfirmedLimit;
        if (save && LettersEnabled.IsChecked == true
            && (!int.TryParse(LettersDailyLimit.Text, out limit) || limit is < 1 or > 10))
        { ShowLettersError("letters.invalidLimit"); return; }
        _lettersSettingsBusy = true; LettersEnabled.IsEnabled = false; LettersSave.IsEnabled = false;
        var draft = LettersDailyLimit.Text;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var status = await projection.LettersSettingsAsync(save ? LettersEnabled.IsChecked == true : null, limit, timeout.Token);
            if (_shutdownStarted || !ReferenceEquals(_dailyApprovalProjection, projection)) return;
            _lettersConfirmedLimit = status.DailyLimit;
            LettersEnabled.IsChecked = status.Enabled;
            if (!background || LettersDailyLimit.Text == draft && !LettersDailyLimit.IsKeyboardFocusWithin)
                LettersDailyLimit.Text = status.DailyLimit.ToString();
            _lettersDisplayStatus = status; _lettersDisplayError = null;
            RenderLettersStatus();
        }
        catch
        {
            if (!_shutdownStarted && ReferenceEquals(_dailyApprovalProjection, projection))
                ShowLettersError("letters.failed");
        }
        finally { _lettersSettingsBusy = false; LettersEnabled.IsEnabled = true; LettersSave.IsEnabled = true; }
    }
}
