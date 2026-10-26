using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace LilithCursorGuard;

[BepInPlugin("lilith.automatic.cursor-guard", "Lilith Automatic Cursor Guard", "1.0.0")]
[BepInProcess("Lilith.exe")]
// If the original add-on is present it gets first ownership; never double-chain it.
[BepInDependency("community.lilith.cursorguard", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class GuardPlugin : BasePlugin
{
    public override void Load()
    {
        try
        {
            CursorHookInstallResult result = NativeCursorGuard.Install(Log);
            if (result is CursorHookInstallResult.Installed or CursorHookInstallResult.AlreadyInstalled)
                Log.LogInfo("cursor_guard.active: automatic foreground protection; no settings required.");
            else
                Log.LogWarning($"cursor_guard.not_active: {result}; no automatic retry.");
        }
        catch (Exception ex)
        {
            // Type only: diagnostics must not contain paths, window titles or player data.
            Log.LogWarning($"cursor_guard.unavailable: {ex.GetType().Name}; original game behavior retained.");
        }
    }

    // A callback address is retained by native Unity for this process lifetime.
    public override bool Unload() => false;
}
