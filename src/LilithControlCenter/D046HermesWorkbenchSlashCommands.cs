using LilithRuntimeContracts;

namespace LilithControlCenter;

internal sealed partial class D046HermesWorkbenchViewModel
{
    private bool _composerCommandInFlight;
    private readonly D046HermesCapabilityCatalogReadCache<HermesCommandCatalogSnapshot>
        _composerCommandCatalogCache = new();

    internal bool CanBrowseComposerCommands => _isConnected && _nativeSurfaceAttested
        && !_projectionFault && !_sequenceGapDetected && !_lifetime.IsCancellationRequested
        && !HasPendingClarification && !HasPendingSecureInput && !HasPendingApproval;

    internal bool HasCurrentComposerCommandCatalog => CanBrowseComposerCommands
        && _composerCommandCatalogCache.CanReuse;

    internal IReadOnlyList<D046HermesSlashSuggestion> ComposerCommandSuggestions
        => HasCurrentComposerCommandCatalog
            ? D046HermesSlashCommands.Project(_composerCommandCatalogCache.Snapshot!.Commands)
            : Array.Empty<D046HermesSlashSuggestion>();

    internal async Task EnsureComposerCommandCatalogLoadedAsync()
    {
        if (!CanBrowseComposerCommands || _composerCommandCatalogCache.CanReuse
            || !_composerCommandCatalogCache.TryBeginRead(out var ticket)) return;
        try
        {
            var projection = _projection as ID046HermesCommandCatalogProjection
                ?? throw new NotSupportedException("Native command discovery is unavailable.");
            var snapshot = await projection.GetCommandCatalogAsync(_lifetime.Token);
            _composerCommandCatalogCache.TryComplete(ticket, snapshot, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            _composerCommandCatalogCache.Fail(ticket);
        }
        finally
        {
            _composerCommandCatalogCache.Fail(ticket);
            OnPropertyChanged(nameof(HasCurrentComposerCommandCatalog));
            OnPropertyChanged(nameof(ComposerCommandSuggestions));
        }
    }

    private void InvalidateComposerCommandCatalog(bool clear, bool stop)
    {
        if (stop) _composerCommandCatalogCache.Stop();
        else if (clear) _composerCommandCatalogCache.Reset();
        else _composerCommandCatalogCache.Invalidate();
        OnPropertyChanged(nameof(HasCurrentComposerCommandCatalog));
        OnPropertyChanged(nameof(ComposerCommandSuggestions));
    }

    // Null means ordinary chat. A slash-shaped draft must never silently become
    // a provider prompt because a command was unavailable, malformed or stale.
    internal async Task<bool?> TryExecuteComposerCommandAsync()
    {
        if (HasPendingClarification || !D046HermesSlashCommands.TryParse(DraftText, out var command, out var hasArguments))
            return null;
        if (_composerCommandInFlight) return false;
        if (HasPendingImage) return RejectComposerCommand("chat.slash.noAttachments");
        if (hasArguments) return RejectComposerCommand("chat.slash.noArguments");
        var row = ComposerCommandSuggestions.FirstOrDefault(value =>
            string.Equals(value.Command, command, StringComparison.OrdinalIgnoreCase));
        if (row is null) return RejectComposerCommand(HasCurrentComposerCommandCatalog
            ? "chat.slash.unsupported" : "chat.slash.unavailable");
        var allowed = row.Action switch
        {
            D046HermesComposerAction.NewChat => CanCreateTask,
            D046HermesComposerAction.Compress => CanCompress,
            D046HermesComposerAction.Usage or D046HermesComposerAction.Context => CanRefreshObservability,
            D046HermesComposerAction.Help => CanBrowseComposerCommands,
            _ => false
        };
        if (!allowed) return RejectComposerCommand("chat.slash.unavailableAction");
        var draft = DraftText;
        var originalTask = SelectedTask;
        _composerCommandInFlight = true;
        try
        {
            var success = row.Action switch
            {
                D046HermesComposerAction.NewChat => await CreateTaskWithSelectionAsync(null),
                D046HermesComposerAction.Compress => await CompressAsync(),
                D046HermesComposerAction.Usage or D046HermesComposerAction.Context => await RefreshObservabilityAsync(),
                D046HermesComposerAction.Help => ShowComposerCommandHelp(),
                _ => false
            };
            // Never erase text typed while a read/mutation was awaiting, or a
            // different chat's draft after navigation. /new intentionally owns
            // its successful selection transition; consume only the unchanged
            // exact command, never any newly typed text.
            if (success && DraftText == draft
                && (ReferenceEquals(SelectedTask, originalTask) || row.Action == D046HermesComposerAction.NewChat))
                DraftText = string.Empty;
            if (success && row.Action is D046HermesComposerAction.Usage or D046HermesComposerAction.Context)
            {
                _statusText = row.Action == D046HermesComposerAction.Usage ? UsageStatusText : ContextStatusText;
                OnPropertyChanged(nameof(StatusText));
            }
            return success;
        }
        finally { _composerCommandInFlight = false; }
    }

    private bool ShowComposerCommandHelp()
    {
        _statusText = ControlCenterLocalization.Current.Format("chat.slash.helpResult",
            string.Join(", ", ComposerCommandSuggestions.Select(row => row.Command)));
        OnPropertyChanged(nameof(StatusText));
        return true;
    }

    private bool RejectComposerCommand(string key)
    {
        _statusText = ControlCenterLocalization.Current[key];
        OnPropertyChanged(nameof(StatusText));
        return false;
    }
}
