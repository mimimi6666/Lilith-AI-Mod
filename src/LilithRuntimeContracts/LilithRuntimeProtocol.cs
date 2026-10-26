using System.Text.Json;

namespace LilithRuntimeContracts;

public static class LilithRuntimeProtocol
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 16 * 1024 * 1024;
    public const int MaximumHermesPayloadUtf8Bytes = MaximumFrameBytes - (64 * 1024);
    public const int MaximumJsonDepth = 64;
    public const int MaximumHermesPayloadJsonDepth = MaximumJsonDepth - 4;
    public const int MaximumVersionUtf8Bytes = 128;
    public const int MaximumMethodUtf8Bytes = 256;
    public const int MaximumCapabilities = 128;
    public const int MaximumCapabilityUtf8Bytes = 128;
    public const int MaximumStatusCodeUtf8Bytes = 128;
    public const int MaximumStatusMessageUtf8Bytes = 4 * 1024;
    public const int MaximumSessionIdUtf8Bytes = 256;
    public const long MaximumUnixTimeMilliseconds = 253402300799999;

    internal static string KindToken(LilithRuntimeMessageKind kind)
        => kind switch
        {
            LilithRuntimeMessageKind.RuntimeHello => "runtime.hello",
            LilithRuntimeMessageKind.RuntimeStatus => "runtime.status",
            LilithRuntimeMessageKind.HermesEvent => "hermes.event",
            LilithRuntimeMessageKind.RuntimeRequest => "runtime.request",
            LilithRuntimeMessageKind.RuntimeResponse => "runtime.response",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    internal static LilithRuntimeMessageKind ParseKindToken(string value)
        => value switch
        {
            "runtime.hello" => LilithRuntimeMessageKind.RuntimeHello,
            "runtime.status" => LilithRuntimeMessageKind.RuntimeStatus,
            "hermes.event" => LilithRuntimeMessageKind.HermesEvent,
            "runtime.request" => LilithRuntimeMessageKind.RuntimeRequest,
            "runtime.response" => LilithRuntimeMessageKind.RuntimeResponse,
            _ => throw new InvalidDataException("Runtime message kind was rejected.")
        };

    internal static string StateToken(LilithRuntimeState state)
        => state switch
        {
            LilithRuntimeState.Starting => "starting",
            LilithRuntimeState.Ready => "ready",
            LilithRuntimeState.Degraded => "degraded",
            LilithRuntimeState.Stopping => "stopping",
            LilithRuntimeState.Stopped => "stopped",
            LilithRuntimeState.Faulted => "faulted",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    internal static LilithRuntimeState ParseStateToken(string value)
        => value switch
        {
            "starting" => LilithRuntimeState.Starting,
            "ready" => LilithRuntimeState.Ready,
            "degraded" => LilithRuntimeState.Degraded,
            "stopping" => LilithRuntimeState.Stopping,
            "stopped" => LilithRuntimeState.Stopped,
            "faulted" => LilithRuntimeState.Faulted,
            _ => throw new InvalidDataException("Runtime state was rejected.")
        };

    internal static void WriteHermesBody(
        Utf8JsonWriter writer,
        string method,
        JsonElement hermesPayload)
    {
        writer.WriteStartObject();
        writer.WriteString("method", method);
        writer.WritePropertyName("hermesPayload");
        writer.WriteRawValue(hermesPayload.GetRawText());
        writer.WriteEndObject();
    }
}
