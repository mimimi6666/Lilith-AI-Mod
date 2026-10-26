namespace LilithCursorGuard;

internal interface ICursorImportSlot
{
    nint Read();
    bool MakeWritable(out uint previousProtection);
    nint CompareExchange(nint value, nint expected);
    bool RestoreProtection(uint protection);
}

internal enum CursorHookInstallResult
{
    Installed, AlreadyInstalled, ExistingHook, ProtectFailed, SlotChanged,
    RestoreFailedRolledBack, RestoreFailedNotOwned, PageProtectionRestoreFailed
}

internal static class CursorHookTransaction
{
    // One aligned pointer only. Never overwrite or remove a different owner's hook.
    internal static CursorHookInstallResult Install(ICursorImportSlot slot, nint original, nint hook)
    {
        if (original == 0 || hook == 0 || original == hook)
            throw new ArgumentException("Invalid cursor hook pointers.");
        nint observed = slot.Read();
        if (observed == hook) return CursorHookInstallResult.AlreadyInstalled;
        if (observed != original) return CursorHookInstallResult.ExistingHook;
        if (!slot.MakeWritable(out uint protection)) return CursorHookInstallResult.ProtectFailed;

        nint exchanged;
        try
        {
            exchanged = slot.CompareExchange(hook, original);
        }
        catch
        {
            slot.RestoreProtection(protection);
            throw;
        }
        if (slot.RestoreProtection(protection))
            return exchanged == original ? CursorHookInstallResult.Installed : CursorHookInstallResult.SlotChanged;

        bool rolledBack = exchanged == original && slot.CompareExchange(original, hook) == hook;
        if (!slot.RestoreProtection(protection))
            return CursorHookInstallResult.PageProtectionRestoreFailed;
        return rolledBack ? CursorHookInstallResult.RestoreFailedRolledBack : CursorHookInstallResult.RestoreFailedNotOwned;
    }
}
