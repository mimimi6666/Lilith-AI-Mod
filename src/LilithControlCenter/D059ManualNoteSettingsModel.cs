using LilithRuntime.Configuration;

namespace LilithControlCenter;

internal enum D059ManualNoteSaveOutcome { Saved, Changed, Conflict, Unavailable, InvalidPreferences }
internal sealed record D059ManualNoteSaveResult(
    D059ManualNoteSaveOutcome Outcome, D059ManualNoteLoadResult Readback);

internal sealed class D059ManualNoteSettingsModel
{
    private readonly D059ManualNotePreferencesStore _store;
    internal D059ManualNoteSettingsModel(D059ManualNotePreferencesStore store) => _store = store;
    internal D059ManualNoteLoadResult Load() => _store.Load();
    internal static bool SelectionForEditor(bool? selected) => selected ?? false;

    internal D059ManualNoteSaveResult Save(long expectedRevision, bool enabled, DateTimeOffset now)
    {
        var saved = _store.TryApply(expectedRevision, enabled, now);
        var readback = _store.Load();
        if (readback.RequiresExplicitRepair)
            return new(D059ManualNoteSaveOutcome.InvalidPreferences, readback);
        if (saved.Admission == D059ManualNoteApplyAdmission.RevisionConflict)
            return new(D059ManualNoteSaveOutcome.Conflict, readback);
        if (saved.Admission != D059ManualNoteApplyAdmission.Applied
            || readback.Disposition != D059ManualNoteLoadDisposition.Ready)
            return new(D059ManualNoteSaveOutcome.Unavailable, readback);
        return new(readback.Snapshot.Revision == saved.Snapshot.Revision
                && readback.Snapshot.Enabled == enabled
            ? D059ManualNoteSaveOutcome.Saved : D059ManualNoteSaveOutcome.Changed, readback);
    }
}

// Reads never queue behind user writes. A click may wait for one existing read,
// but retains the revision displayed when the user made that choice.
internal sealed class D059ManualNoteSettingsOperations
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _savePending;
    private long _intentEpoch;
    internal bool SavePending => Volatile.Read(ref _savePending) != 0;
    internal long IntentEpoch => Interlocked.Read(ref _intentEpoch);
    internal bool CanPublishRead(long epoch) => epoch == IntentEpoch && !SavePending;

    internal async Task<D059ManualNoteLoadResult?> ReadAsync(Func<Task<D059ManualNoteLoadResult>> load)
    {
        if (SavePending || !_gate.Wait(0)) return null;
        try { return await load(); }
        finally { _gate.Release(); }
    }

    internal async Task<D059ManualNoteSaveResult?> SaveAsync(
        long expectedRevision, bool selected,
        Func<long, bool, Task<D059ManualNoteSaveResult>> save)
    {
        if (Interlocked.CompareExchange(ref _savePending, 1, 0) != 0) return null;
        Interlocked.Increment(ref _intentEpoch);
        await _gate.WaitAsync();
        try { return await save(expectedRevision, selected); }
        finally
        {
            _gate.Release();
            Volatile.Write(ref _savePending, 0);
        }
    }
}
