namespace LilithRuntimeContracts;

// Safe process-boundary codes. Never carry provider exceptions or config text.
public static class LilithRuntimeProcessExit
{
    public const int Normal = 0;
    public const int Failed = 1;
    public const int AlreadyRunning = 3;
    public const int ConfigVerificationFailed = 31;
    public const int HermesComponentLayoutMismatch = 40;
    public const int HermesComponentHashMismatch = 41;
    public const int HermesComponentMissingOrUnreadable = 42;
    public const int CuaComponentVerificationFailed = 43;
    public const int VoiceComponentVerificationFailed = 44;
    public const int ProfileVerificationFailed = 45;
    public const int GatewayReadyTimeout = 46;
    public const int GatewayStartupFailed = 47;
    public const int NativeSurfaceVerificationFailed = 48;

    public static int ForFailure(LilithRuntimeFailureKind kind) => kind switch
    {
        LilithRuntimeFailureKind.ConfigVerificationFailed => ConfigVerificationFailed,
        LilithRuntimeFailureKind.HermesComponentLayoutMismatch => HermesComponentLayoutMismatch,
        LilithRuntimeFailureKind.HermesComponentHashMismatch => HermesComponentHashMismatch,
        LilithRuntimeFailureKind.HermesComponentMissingOrUnreadable => HermesComponentMissingOrUnreadable,
        LilithRuntimeFailureKind.CuaComponentVerificationFailed => CuaComponentVerificationFailed,
        LilithRuntimeFailureKind.VoiceComponentVerificationFailed => VoiceComponentVerificationFailed,
        LilithRuntimeFailureKind.ProfileVerificationFailed => ProfileVerificationFailed,
        LilithRuntimeFailureKind.GatewayReadyTimeout => GatewayReadyTimeout,
        LilithRuntimeFailureKind.GatewayStartupFailed => GatewayStartupFailed,
        LilithRuntimeFailureKind.NativeSurfaceVerificationFailed => NativeSurfaceVerificationFailed,
        _ => Failed
    };
}

public enum LilithRuntimeFailureKind
{
    Unspecified = 0,
    ConfigVerificationFailed = 1,
    HermesComponentLayoutMismatch = 2,
    HermesComponentHashMismatch = 3,
    HermesComponentMissingOrUnreadable = 4,
    CuaComponentVerificationFailed = 5,
    VoiceComponentVerificationFailed = 6,
    ProfileVerificationFailed = 7,
    GatewayReadyTimeout = 8,
    GatewayStartupFailed = 9,
    NativeSurfaceVerificationFailed = 10
}
