using LilithRuntimeContracts;

namespace LilithRuntime;

internal static class LilithRuntimeStartupFailure
{
    // The call site selects the stage. Text from providers, paths or exceptions
    // is not classification input; a more precise rejection must survive.
    internal static LilithRuntimeException Classify(
        Exception exception,
        LilithRuntimeFailureKind fallback)
    {
        if (exception is LilithRuntimeException specific
            && specific.FailureKind != LilithRuntimeFailureKind.Unspecified)
            return specific;
        if (exception is not (LilithRuntimeException or IOException
            or UnauthorizedAccessException or System.Security.SecurityException
            or System.Text.Json.JsonException or FormatException or OverflowException
            or TimeoutException or System.Net.WebSockets.WebSocketException
            or System.ComponentModel.Win32Exception or TaskCanceledException))
            return new LilithRuntimeException("Runtime startup failed.");
        return new LilithRuntimeException(
            exception is LilithRuntimeException known ? known.Message : "Runtime startup stage failed.",
            fallback);
    }
}
