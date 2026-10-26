namespace LilithTextInjector;

public enum D041RuntimeAuthorityStartupState
{
    Enabled,
    HardStopped
}

public sealed record D041RuntimeAuthorityStartupDecision(
    D041RuntimeAuthorityStartupState State,
    bool MayContinueRuntimeInitialization,
    int ProtectedIoInitializations,
    int StateInitializations,
    int SensingInitializations,
    int TelemetryInitializations,
    int ProviderInitializations,
    int HarmonyPatchInitializations,
    string Classification);

public static class D041RuntimeAuthorityStartupPolicy
{
    public const string EnabledClassification =
        "RuntimeAuthorityEnabled";
    public const string HardStopClassification =
        "P2RuntimeAuthorityHardStopped";

    public static D041RuntimeAuthorityStartupDecision Evaluate(bool enabled) =>
        enabled
            ? new D041RuntimeAuthorityStartupDecision(
                D041RuntimeAuthorityStartupState.Enabled,
                true,
                0,
                0,
                0,
                0,
                0,
                0,
                EnabledClassification)
            : new D041RuntimeAuthorityStartupDecision(
                D041RuntimeAuthorityStartupState.HardStopped,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                HardStopClassification);
}
