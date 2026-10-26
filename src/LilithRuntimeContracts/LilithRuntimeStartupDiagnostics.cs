namespace LilithRuntimeContracts;

// A closed process-boundary catalog. Never classify exception text or return it
// as a diagnostic. Exit codes convey observation only, never readiness or trust.
public static class LilithRuntimeStartupDiagnostics
{
    private static readonly IReadOnlyDictionary<int, string> ExitCodes =
        new Dictionary<int, string>
        {
            [LilithRuntimeProcessExit.Normal] = "runtime.exited_normally",
            [LilithRuntimeProcessExit.Failed] = "runtime.startup_failed",
            [LilithRuntimeProcessExit.AlreadyRunning] = "runtime.already_running",
            [LilithRuntimeProcessExit.ConfigVerificationFailed] = "runtime.config_verification_failed",
            [LilithRuntimeProcessExit.HermesComponentLayoutMismatch] = "runtime.hermes_component_layout_mismatch",
            [LilithRuntimeProcessExit.HermesComponentHashMismatch] = "runtime.hermes_component_hash_mismatch",
            [LilithRuntimeProcessExit.HermesComponentMissingOrUnreadable] = "runtime.hermes_component_missing_or_unreadable",
            [LilithRuntimeProcessExit.CuaComponentVerificationFailed] = "runtime.cua_component_verification_failed",
            [LilithRuntimeProcessExit.VoiceComponentVerificationFailed] = "runtime.voice_component_verification_failed",
            [LilithRuntimeProcessExit.ProfileVerificationFailed] = "runtime.profile_verification_failed",
            [LilithRuntimeProcessExit.GatewayReadyTimeout] = "runtime.gateway_ready_timeout",
            [LilithRuntimeProcessExit.GatewayStartupFailed] = "runtime.gateway_startup_failed",
            [LilithRuntimeProcessExit.NativeSurfaceVerificationFailed] = "runtime.native_surface_verification_failed"
        };

    public static IReadOnlyList<string> KnownCodes { get; } =
        Array.AsReadOnly(ExitCodes.Values.ToArray());

    public static bool IsKnownCode(string? code) => code is not null
        && KnownCodes.Contains(code, StringComparer.Ordinal);

    public static string CodeForExit(int exitCode) =>
        ExitCodes.TryGetValue(exitCode, out var code) ? code : "runtime.startup_failed";

    public static string StageForCode(string? code) => code switch
    {
        "runtime.hermes_component_layout_mismatch" or
        "runtime.hermes_component_hash_mismatch" or
        "runtime.hermes_component_missing_or_unreadable" => "hermes_component",
        "runtime.cua_component_verification_failed" => "cua_component",
        "runtime.voice_component_verification_failed" => "voice_component",
        "runtime.config_verification_failed" or
        "runtime.profile_verification_failed" => "profile",
        "runtime.gateway_ready_timeout" => "gateway_ready",
        "runtime.gateway_startup_failed" => "gateway_start",
        "runtime.native_surface_verification_failed" => "gateway_attestation",
        "runtime.exited_normally" => "exit",
        "runtime.already_running" => "single_instance",
        _ => "unknown"
    };
}
