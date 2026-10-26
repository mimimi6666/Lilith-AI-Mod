using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithControlCenter;

internal sealed class ControlCenterClientRequest
{
    public int ProtocolVersion { get; set; }
    public int Kind { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string ExpectedRuntimeFingerprint { get; set; } = string.Empty;
    public string ExpectedSessionId { get; set; } = string.Empty;
    public long RequestedAtUnixMilliseconds { get; set; }
    public long ExpiresAtUnixMilliseconds { get; set; }
}

internal sealed class ControlCenterClientSetting
{
    public string SettingId { get; set; } = string.Empty;
    public int State { get; set; }
    public int ValueKind { get; set; }
    public bool? BooleanValue { get; set; }
    public long? IntegerValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public string SafeTokenValue { get; set; } = string.Empty;
}

internal sealed class ControlCenterClientFeature
{
    public string FeatureId { get; set; } = string.Empty;
    public int State { get; set; }
}

internal sealed class ControlCenterClientMetric
{
    public string Id { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int Health { get; set; }
}

internal sealed class ControlCenterClientTelemetry
{
    public long ObservedAtUnixMilliseconds { get; set; }
    public string IntegrationState { get; set; } = string.Empty;
    public string Interruptibility { get; set; } = string.Empty;
    public string InterruptibilityReason { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public ControlCenterClientMetric[] Metrics { get; set; } =
        Array.Empty<ControlCenterClientMetric>();
}

internal sealed class ControlCenterClientStatus
{
    public int SchemaVersion { get; set; }
    public int DescriptorSetVersion { get; set; }
    public string SchemaFingerprint { get; set; } = string.Empty;
    public string RuntimeFingerprint { get; set; } = string.Empty;
    public long RuntimeGeneration { get; set; }
    public string RuntimeSessionId { get; set; } = string.Empty;
    public long ConfigurationGeneration { get; set; }
    public long ObservedAtUnixMilliseconds { get; set; }
    public string SemanticFingerprint { get; set; } = string.Empty;
    public string ObservationFingerprint { get; set; } = string.Empty;
    public ControlCenterClientSetting[] Settings { get; set; } =
        Array.Empty<ControlCenterClientSetting>();
    public ControlCenterClientFeature[] Features { get; set; } =
        Array.Empty<ControlCenterClientFeature>();
    public ControlCenterClientTelemetry? Telemetry { get; set; }
}

internal sealed class ControlCenterClientResponse
{
    public int ProtocolVersion { get; set; }
    public int Admission { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public ControlCenterClientStatus? Status { get; set; }
}

internal static class ControlCenterReadOnlyClient
{
    internal const string PipeName = "lilith-control-center-v1";
    internal const int ProtocolVersion = 1;
    internal const int MaximumFrameBytes = 64 * 1024;
    internal static readonly TimeSpan ConnectionDeadline =
        TimeSpan.FromMilliseconds(1500);
    internal static readonly TimeSpan RequestLifetime =
        TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    internal static bool IsRecoverableReadFailure(Exception exception)
        => exception is TimeoutException
            or OperationCanceledException
            or InvalidOperationException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or JsonException;

    internal static async Task<ControlCenterClientResponse> ReadOnceAsync(
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        deadline.CancelAfter(ConnectionDeadline);
        using var client = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var request = new ControlCenterClientRequest
        {
            ProtocolVersion = ProtocolVersion,
            Kind = 1,
            RequestId = "ui-" + Guid.NewGuid().ToString("N"),
            Sequence = 1,
            RequestedAtUnixMilliseconds = now.ToUnixTimeMilliseconds(),
            ExpiresAtUnixMilliseconds =
                now.Add(RequestLifetime).ToUnixTimeMilliseconds()
        };
        await WriteFrameAsync(
            client,
            JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions),
            deadline.Token).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<ControlCenterClientResponse>(
            await ReadFrameAsync(client, deadline.Token).ConfigureAwait(false),
            JsonOptions);
        if (response == null)
            throw new InvalidDataException("控制中心回應為空。");
        if (response.ProtocolVersion != ProtocolVersion
            || response.Admission != 1
            || response.Status == null)
        {
            throw new InvalidDataException(
                "控制中心拒絕唯讀狀態要求。Admission="
                + response.Admission);
        }
        return response;
    }

    internal static async Task WriteFrameAsync(
        Stream stream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        ValidateFrame(frame);
        var prefix = BitConverter.GetBytes(frame.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken)
            .ConfigureAwait(false);
        var length = BitConverter.ToInt32(prefix, 0);
        if (length <= 0 || length > MaximumFrameBytes)
            throw new InvalidDataException("控制中心訊框超出安全邊界。");
        var frame = new byte[length];
        await ReadExactlyAsync(stream, frame, cancellationToken)
            .ConfigureAwait(false);
        return frame;
    }

    private static void ValidateFrame(byte[]? frame)
    {
        if (frame == null
            || frame.Length == 0
            || frame.Length > MaximumFrameBytes)
        {
            throw new InvalidDataException("控制中心訊框超出安全邊界。");
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }
}
