namespace LilithControlCenter;

// UI state only. Drafts never write files or become model instructions by themselves.
internal sealed class D048ExpressionEditorState
{
    internal D048ExpressionCatalog? Catalog { get; private set; }
    internal D048ExpressionVersion? Preview { get; private set; }
    internal string Draft { get; private set; } = "";
    internal string Reason { get; private set; } = "";
    internal bool IsBusy { get; private set; }
    internal bool NeedsRefresh { get; private set; } = true;
    internal bool MutationInFlight { get; private set; }
    internal bool HasUnconfirmedMutation { get; private set; }
    internal long Epoch { get; private set; }
    internal bool IsDirty => Draft != (Catalog?.Content ?? "") || Reason.Length != 0;
    internal bool CanSave => !IsBusy && !NeedsRefresh && Catalog is { Revision: < D048ExpressionLimits.MaximumRevisions }
        && Draft != Catalog.Content && ValidDraft;
    internal bool CanRestore => !IsBusy && !NeedsRefresh && !IsDirty
        && Catalog is { Revision: < D048ExpressionLimits.MaximumRevisions }
        && Preview is not null && Preview.Revision < Catalog.Revision;
    internal bool ValidDraft
    {
        get
        {
            try
            {
                D048ExpressionLimits.Text(Draft, D048ExpressionLimits.MaximumContentBytes);
                D048ExpressionLimits.Text(Reason, D048ExpressionLimits.MaximumReasonBytes, reason: true);
                return true;
            }
            catch (ArgumentException) { return false; }
        }
    }
    internal void Edit(string draft, string reason)
    { if (!IsBusy) { Draft = draft; Reason = reason; } }
    internal long Begin(bool mutation = false)
    {
        if (IsBusy) throw new InvalidOperationException("Expression operation already active.");
        IsBusy = true;
        MutationInFlight = mutation;
        return ++Epoch;
    }
    internal bool ApplyCatalog(long epoch, D048ExpressionCatalog catalog, bool mutation)
    {
        if (!IsBusy || epoch != Epoch || Catalog is not null && (catalog.Revision < Catalog.Revision
            || catalog.Revision == Catalog.Revision && catalog.ContentSha256 != Catalog.ContentSha256)) return false;
        var keepDraft = !mutation && IsDirty;
        Catalog = catalog;
        NeedsRefresh = false;
        HasUnconfirmedMutation = false;
        if (!keepDraft) { Draft = catalog.Content; Reason = ""; }
        if (Preview is not null && Preview.BaseIdentityFingerprint != catalog.BaseIdentityFingerprint) Preview = null;
        return true;
    }
    internal bool ApplyPreview(long epoch, D048ExpressionVersion version)
    {
        if (!IsBusy || epoch != Epoch || Catalog is null || version.Revision > Catalog.Revision
            || version.BaseIdentityFingerprint != Catalog.BaseIdentityFingerprint) return false;
        var expectedHash = version.Revision == 0 ? D048ExpressionLimits.Hash("")
            : Catalog.History.SingleOrDefault(h => h.Revision == version.Revision)?.ContentSha256;
        if (expectedHash != version.ContentSha256) return false;
        Preview = version;
        return true;
    }
    internal void Finish(long epoch) { if (epoch == Epoch) { IsBusy = false; MutationInFlight = false; } }
    internal void RequireRefresh(long epoch, bool resultUncertain = false)
    { if (epoch == Epoch) { NeedsRefresh = true; HasUnconfirmedMutation |= resultUncertain; } }
    internal string PresentStatus(string statusKey) => HasUnconfirmedMutation ? "expression.unknown" : statusKey;
    internal void Detach()
    {
        HasUnconfirmedMutation |= MutationInFlight;
        ++Epoch;
        IsBusy = false;
        MutationInFlight = false;
        NeedsRefresh = true;
        // The last accepted snapshot and unsaved draft survive reconnection.
    }
    internal void DiscardDraft()
    { if (!IsBusy) { Draft = Catalog?.Content ?? ""; Reason = ""; } }
}
