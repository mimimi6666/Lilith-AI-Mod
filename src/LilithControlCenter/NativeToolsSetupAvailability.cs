using LilithRuntimeContracts;

namespace LilithControlCenter;

// Presentation eligibility for opening a fresh setup chat, never tool permission.
// The next chat must still receive native tool-surface attestation before use.
internal static class NativeToolsSetupAvailability
{
    internal static bool HasSetupHelper(HermesNativeToolsCatalog? catalog)
        => catalog is not null && catalog.Toolsets.Any(row => row.Enabled && row.Configured
            && row.Readiness == HermesNativeToolsetReadiness.Ready
            && catalog.EnabledToolsets.Contains(row.Name, StringComparer.Ordinal)
            && row.Tools.Contains("native_tool_setup", StringComparer.Ordinal));
}
