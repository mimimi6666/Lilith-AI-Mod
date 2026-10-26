using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

public partial class D048ExpressionPanel : UserControl
{
    private readonly D048ExpressionEditorState _state = new();
    private ID048ExpressionProjection? _projection;
    private CancellationTokenSource? _operation;
    private bool _rendering;
    private string _statusKey = "expression.notLoaded";
    private int _historyRevision = -1;
    private Action? _confirmExit;
    private string _historyLanguage = "";
    internal Action? EnsureConnection { get; set; }
    internal Func<Task>? ReconcileConnection { get; set; }
    internal bool IsOperationBusy => _state.IsBusy;
    internal bool HasUnsavedDraft => _state.IsDirty;
    // Drafts are owned by this panel and retained across a replacement pipe.
    internal bool CanRetireExpressionProjection => !_state.IsBusy;
    private static ControlCenterLocalization Texts => ControlCenterLocalization.Current;

    public D048ExpressionPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => { Texts.LanguageChanged -= RefreshLanguage; Texts.LanguageChanged += RefreshLanguage; Render(); };
        Unloaded += (_, _) => Texts.LanguageChanged -= RefreshLanguage;
        Render();
    }
    internal void AttachExpressionProjection(ID048ExpressionProjection projection)
    { _projection = projection; Render(); }
    internal void DetachExpressionProjection()
    {
        _projection = null;
        _state.Detach();
        _operation?.Cancel();
        _statusKey = "expression.reconnected";
        Render();
    }
    internal Task RefreshExpressionAsync() => RunAsync("read");
    internal void ShowExitBusy() { _statusKey = "expression.exitBusy"; Render(); StatusLabel.BringIntoView(); }
    internal void ShowExitConfirmation(Action confirm)
    {
        _confirmExit = confirm;
        ExitConfirmation.Visibility = Visibility.Visible;
        ExitKeepButton.Focus();
        ExitConfirmation.BringIntoView();
    }
    private void CancelExit_Click(object sender, RoutedEventArgs e)
    { _confirmExit = null; ExitConfirmation.Visibility = Visibility.Collapsed; DraftInput.Focus(); }
    private void ConfirmExit_Click(object sender, RoutedEventArgs e)
    {
        if (_state.IsBusy) return;
        var confirm = _confirmExit; _confirmExit = null; ExitConfirmation.Visibility = Visibility.Collapsed; confirm?.Invoke();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshExpressionAsync();
    private async void Save_Click(object sender, RoutedEventArgs e)
    { if (_state.CanSave) await RunAsync("save"); }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    { if (_state.CanRestore) await RunAsync("restore"); }
    private void Discard_Click(object sender, RoutedEventArgs e)
    { _state.DiscardDraft(); _statusKey = "expression.discarded"; Render(); }
    private void DraftChanged(object sender, TextChangedEventArgs e)
    {
        if (_rendering || DraftInput is null || ReasonInput is null) return;
        _state.Edit(DraftInput.Text, ReasonInput.Text);
        if (!_state.NeedsRefresh)
            _statusKey = !_state.ValidDraft ? "expression.invalid" : _state.IsDirty ? "expression.unsaved" : "expression.loaded";
        Render();
    }
    private async void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_rendering && HistorySelector.SelectedItem is HistoryChoice row && !_state.IsBusy)
            await RunAsync("preview", row.Revision);
    }
    private async Task RunAsync(string kind, int revision = 0)
    {
        if (_state.IsBusy) return;
        try { EnsureConnection?.Invoke(); }
        catch (Exception) { _statusKey = "expression.readFailed"; Render(); return; }
        if (_projection is not { } projection) { _statusKey = "expression.readFailed"; Render(); return; }
        if (kind == "save" && !_state.CanSave || kind == "restore" && !_state.CanRestore) return;
        var expected = _state.Catalog?.Revision ?? 0;
        var content = _state.Draft;
        var reason = _state.Reason;
        var target = _state.Preview?.Revision ?? 0;
        var mutation = kind is "save" or "restore";
        var epoch = _state.Begin(mutation);
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _operation = operation;
        _statusKey = mutation ? "expression.saving" : "expression.loading";
        Render();
        try
        {
            if (kind == "preview")
            {
                var version = await projection.GetExpressionVersionAsync(revision, operation.Token);
                if (!_state.ApplyPreview(epoch, version)) throw new InvalidOperationException("Expression preview changed.");
                _statusKey = "expression.previewed";
            }
            else
            {
                var catalog = kind switch
                {
                    "save" => await projection.SaveExpressionAsync("cc-expression-" + Guid.NewGuid().ToString("N"), expected, content, reason, operation.Token),
                    "restore" => await projection.RevertExpressionAsync("cc-expression-" + Guid.NewGuid().ToString("N"), expected, target,
                        Texts.Format("expression.restoreReason", target), operation.Token),
                    _ => await projection.GetExpressionAsync(operation.Token)
                };
                operation.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_projection, projection) || !_state.ApplyCatalog(epoch, catalog, mutation))
                    throw new InvalidOperationException("Expression catalog owner or revision changed.");
                _statusKey = mutation ? "expression.saved" : _state.IsDirty ? "expression.refreshedDraft" : "expression.loaded";
            }
        }
        catch (D048ExpressionOperationException exception)
        {
            if (_state.Epoch == epoch)
            {
                _state.RequireRefresh(epoch, mutation && exception.Code is ("expression.operation_failed" or "expression.result_uncertain"));
                _statusKey = exception.Code switch
                {
                    "expression.revision_conflict" => "expression.conflict",
                    "expression.secret_rejected" => "expression.secretRejected",
                    "expression.capacity_reached" => "expression.capacity",
                    "expression.invalid_request" => "expression.invalid",
                    "expression.version_not_found" => "expression.versionMissing",
                    _ => mutation ? "expression.unknown" : "expression.readFailed"
                };
            }
        }
        catch (Exception)
        {
            if (_state.Epoch == epoch)
            { _state.RequireRefresh(epoch, resultUncertain: mutation); _statusKey = mutation ? "expression.unknown" : "expression.readFailed"; }
        }
        finally
        {
            _state.Finish(epoch);
            if (ReferenceEquals(_operation, operation)) _operation = null;
            Render();
        }
        // Shared transport recovery retains this panel's draft. Never retry a mutation.
        if (ReconcileConnection is not null)
        {
            try { await ReconcileConnection(); }
            catch (Exception) { /* The accepted snapshot and draft remain in the editor. */ }
        }
    }
    private void RefreshLanguage()
    { if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(RefreshLanguage); else Render(); }
    private void Render()
    {
        if (DraftInput is null) return;
        _rendering = true;
        try
        {
            if (DraftInput.Text != _state.Draft) DraftInput.Text = _state.Draft;
            if (ReasonInput.Text != _state.Reason) ReasonInput.Text = _state.Reason;
            DraftInput.IsReadOnly = ReasonInput.IsReadOnly = _state.IsBusy;
            SaveButton.IsEnabled = _projection is not null && _state.CanSave;
            RefreshButton.IsEnabled = !_state.IsBusy;
            DiscardButton.IsEnabled = !_state.IsBusy && _state.IsDirty;
            HistorySelector.IsEnabled = !_state.IsBusy && _state.Catalog is not null && !_state.NeedsRefresh;
            RestoreButton.IsEnabled = _projection is not null && _state.CanRestore;
            ExitDiscardButton.IsEnabled = !_state.IsBusy;
            VersionLabel.Text = _state.Catalog is { } catalog ? Texts.Format("expression.version", catalog.Revision) : Texts["expression.notLoaded"];
            StatusLabel.Text = Texts[_state.PresentStatus(_statusKey)];
            PreviewInput.Text = _state.Preview?.Content ?? "";
            if (_state.Catalog is { } current && (_historyRevision != current.Revision || _historyLanguage != Texts.Language))
            {
                var selected = (HistorySelector.SelectedItem as HistoryChoice)?.Revision;
                var choices = current.History.Reverse().Select(h => new HistoryChoice(h.Revision,
                    Texts.Format("expression.historyRow", h.Revision, h.CreatedAtUtc, h.Reason))).ToList();
                choices.Add(new(0, Texts["expression.baseline"]));
                HistorySelector.ItemsSource = choices;
                if (selected is not null) HistorySelector.SelectedValue = selected;
                _historyRevision = current.Revision;
                _historyLanguage = Texts.Language;
            }
        }
        finally { _rendering = false; }
    }
    private sealed record HistoryChoice(int Revision, string Label);
}
