using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    private LilithRuntimeResponse NativeConfigQuarantineFailure(
        string method, string message, int? hermesCode = null)
    {
        var diagnostic = _modelAuthority.Snapshot().NativeModelFailure;
        return Failure(method, "runtime.native_config_quarantined",
            diagnostic is null ? message : message + " [" + diagnostic.SafeSummary + "]",
            hermesCode);
    }
}
