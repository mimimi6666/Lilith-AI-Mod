using LilithRuntimeContracts;

namespace LilithRuntime;

// Smoke is also a writer, including when launched outside the desktop UI.
// Temporary isolated profiles do not contend with the real player's service.
internal sealed class LilithPlayerSaveServiceLease : IDisposable
{
    private readonly LilithPlayerSaveMaintenanceLease _profileLease;
    private readonly ILilithRuntimeSingleInstanceLease? _legacyLease;

    private LilithPlayerSaveServiceLease(LilithPlayerSaveMaintenanceLease profileLease,
        ILilithRuntimeSingleInstanceLease? legacyLease)
    { _profileLease = profileLease; _legacyLease = legacyLease; }

    internal static LilithPlayerSaveServiceLease? TryAcquire(string profile, bool dedicatedDefaultProfile,
        Func<ILilithRuntimeSingleInstanceLease?>? acquireLegacy = null)
    {
        var fileLease = LilithPlayerSaveMaintenanceLease.TryAcquire(profile);
        if (fileLease is null) return null;
        try
        {
            var legacyLease = dedicatedDefaultProfile
                ? (acquireLegacy ?? LilithRuntimeSingleInstanceLease.TryAcquireCurrentUser)() : null;
            if (dedicatedDefaultProfile && legacyLease is null) { fileLease.Dispose(); return null; }
            return new(fileLease, legacyLease);
        }
        catch { fileLease.Dispose(); throw; }
    }

    public void Dispose()
    {
        try { _legacyLease?.Dispose(); }
        finally { _profileLease.Dispose(); }
    }
}
