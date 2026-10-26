using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum ControlCenterTransportRequestKind
{
    Invalid = 0,
    Hello = 1,
    ReadStatus = 2,
    Close = 3,
    SubmitSettings = 4
}

internal enum ControlCenterTransportAdmission
{
    Invalid = 0,
    Accepted = 1,
    AcceptedAndClosed = 2,
    ProtocolVersionMismatch = 3,
    InvalidRequest = 4,
    RequestNotYetValid = 5,
    RequestExpired = 6,
    DuplicateRequest = 7,
    NonMonotonicSequence = 8,
    RuntimeIdentityMismatch = 9,
    SessionIdentityMismatch = 10,
    MutationDeniedByStageCBoundary = 11,
    ProjectionUnavailable = 12,
    CapacityReached = 13,
    SessionClosed = 14,
    FrameRejected = 15
}

internal sealed class ControlCenterStageCRequest
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

internal sealed class ControlCenterStageCWireSetting
{
    public string SettingId { get; set; } = string.Empty;
    public int State { get; set; }
    public int ValueKind { get; set; }
    public bool? BooleanValue { get; set; }
    public long? IntegerValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public string SafeTokenValue { get; set; } = string.Empty;
}

internal sealed class ControlCenterStageCWireFeature
{
    public string FeatureId { get; set; } = string.Empty;
    public int State { get; set; }
}

internal sealed class ControlCenterStageCWireMetric
{
    public string Id { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int Health { get; set; }
}

internal sealed class ControlCenterStageCWireTelemetry
{
    public long ObservedAtUnixMilliseconds { get; set; }
    public string IntegrationState { get; set; } = string.Empty;
    public string Interruptibility { get; set; } = string.Empty;
    public string InterruptibilityReason { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public ControlCenterStageCWireMetric[] Metrics { get; set; } =
        Array.Empty<ControlCenterStageCWireMetric>();
}

internal sealed class ControlCenterStageCStatus
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
    public ControlCenterStageCWireSetting[] Settings { get; set; } =
        Array.Empty<ControlCenterStageCWireSetting>();
    public ControlCenterStageCWireFeature[] Features { get; set; } =
        Array.Empty<ControlCenterStageCWireFeature>();
    public ControlCenterStageCWireTelemetry? Telemetry { get; set; }
}

internal sealed class ControlCenterStageCResponse
{
    public int ProtocolVersion { get; set; }
    public int Admission { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public ControlCenterStageCStatus? Status { get; set; }
}

internal sealed class ControlCenterStageCSession
{
    internal const int ProtocolVersion = 1;
    internal const int MaximumRequests = 32;

    private readonly HashSet<string> _requestIds =
        new(StringComparer.Ordinal);
    private long _lastSequence;
    private bool _closed;

    internal ControlCenterStageCResponse Handle(
        ControlCenterStageCRequest? request,
        LegacyControlCenterProjection? projection,
        DateTimeOffset now)
    {
        if (_closed)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.SessionClosed);
        }
        if (request == null)
        {
            return Response(
                null,
                ControlCenterTransportAdmission.InvalidRequest);
        }
        if (request.ProtocolVersion != ProtocolVersion)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.ProtocolVersionMismatch);
        }
        if (!TryValidateRequestId(request.RequestId))
        {
            return Response(
                request,
                ControlCenterTransportAdmission.InvalidRequest);
        }
        if (_requestIds.Contains(request.RequestId))
        {
            return Response(
                request,
                ControlCenterTransportAdmission.DuplicateRequest);
        }
        if (_requestIds.Count >= MaximumRequests)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.CapacityReached);
        }
        if (request.Sequence <= _lastSequence)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.NonMonotonicSequence);
        }
        DateTimeOffset requestedAt;
        DateTimeOffset expiresAt;
        try
        {
            requestedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                request.RequestedAtUnixMilliseconds);
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(
                request.ExpiresAtUnixMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.InvalidRequest);
        }
        if (expiresAt <= requestedAt)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.InvalidRequest);
        }
        if (now < requestedAt)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.RequestNotYetValid);
        }
        if (now >= expiresAt)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.RequestExpired);
        }

        _requestIds.Add(request.RequestId);
        _lastSequence = request.Sequence;
        var kind = (ControlCenterTransportRequestKind)request.Kind;
        if (kind == ControlCenterTransportRequestKind.SubmitSettings)
        {
            return Response(
                request,
                ControlCenterTransportAdmission
                    .MutationDeniedByStageCBoundary);
        }
        if (kind == ControlCenterTransportRequestKind.Close)
        {
            _closed = true;
            return Response(
                request,
                ControlCenterTransportAdmission.AcceptedAndClosed);
        }
        if (kind != ControlCenterTransportRequestKind.Hello
            && kind != ControlCenterTransportRequestKind.ReadStatus)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.InvalidRequest);
        }
        if (projection == null)
        {
            return Response(
                request,
                ControlCenterTransportAdmission.ProjectionUnavailable);
        }
        if (kind == ControlCenterTransportRequestKind.ReadStatus)
        {
            if (!string.Equals(
                    request.ExpectedRuntimeFingerprint,
                    projection.EffectiveSnapshot.RuntimeIdentity.Fingerprint,
                    StringComparison.Ordinal))
            {
                return Response(
                    request,
                    ControlCenterTransportAdmission.RuntimeIdentityMismatch);
            }
            if (!string.Equals(
                    request.ExpectedSessionId,
                    projection.EffectiveSnapshot.RuntimeIdentity
                        .SessionId.Value,
                    StringComparison.Ordinal))
            {
                return Response(
                    request,
                    ControlCenterTransportAdmission.SessionIdentityMismatch);
            }
        }
        return Response(
            request,
            ControlCenterTransportAdmission.Accepted,
            Status(projection));
    }

    private static bool TryValidateRequestId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;
        return value.All(
            character => char.IsLetterOrDigit(character)
                || character == '-'
                || character == '_'
                || character == '.');
    }

    private static ControlCenterStageCResponse Response(
        ControlCenterStageCRequest? request,
        ControlCenterTransportAdmission admission,
        ControlCenterStageCStatus? status = null)
        => new()
        {
            ProtocolVersion = ProtocolVersion,
            Admission = (int)admission,
            RequestId = request?.RequestId ?? string.Empty,
            Sequence = request?.Sequence ?? 0,
            Status = status
        };

    private static ControlCenterStageCStatus Status(
        LegacyControlCenterProjection projection)
        => new()
        {
            SchemaVersion =
                projection.EffectiveSnapshot.SchemaVersion,
            DescriptorSetVersion =
                projection.EffectiveSnapshot.DescriptorSetVersion,
            SchemaFingerprint =
                projection.EffectiveSnapshot.SchemaFingerprint,
            RuntimeFingerprint =
                projection.EffectiveSnapshot.RuntimeIdentity.Fingerprint,
            RuntimeGeneration =
                projection.EffectiveSnapshot.RuntimeIdentity
                    .RuntimeGeneration,
            RuntimeSessionId =
                projection.EffectiveSnapshot.RuntimeIdentity.SessionId.Value,
            ConfigurationGeneration =
                projection.EffectiveSnapshot.ConfigurationGeneration,
            ObservedAtUnixMilliseconds =
                projection.EffectiveSnapshot.ObservedAt
                    .ToUnixTimeMilliseconds(),
            SemanticFingerprint = projection.SemanticFingerprint,
            ObservationFingerprint = projection.ObservationFingerprint,
            Settings = projection.EffectiveSnapshot.EffectiveValues
                .Select(
                    setting => new ControlCenterStageCWireSetting
                    {
                        SettingId = setting.SettingId.Value,
                        State = (int)setting.State,
                        ValueKind = setting.Value == null
                            ? (int)ControlCenterSettingValueKind.Invalid
                            : (int)setting.Value.Kind,
                        BooleanValue = setting.Value?.BooleanValue,
                        IntegerValue = setting.Value?.IntegerValue,
                        DecimalValue = setting.Value?.DecimalValue,
                        SafeTokenValue =
                            setting.Value?.SafeTokenValue ?? string.Empty
                    })
                .ToArray(),
            Features = projection.EffectiveSnapshot.FeatureStates
                .Select(
                    feature => new ControlCenterStageCWireFeature
                    {
                        FeatureId = feature.FeatureId.Value,
                        State = (int)feature.State
                    })
                .ToArray(),
            Telemetry = projection.RuntimeTelemetry == null
                ? null
                : new ControlCenterStageCWireTelemetry
                {
                    ObservedAtUnixMilliseconds = projection.RuntimeTelemetry
                        .ObservedAt.ToUnixTimeMilliseconds(),
                    IntegrationState = projection.RuntimeTelemetry
                        .IntegrationState,
                    Interruptibility = projection.RuntimeTelemetry
                        .Interruptibility,
                    InterruptibilityReason = projection.RuntimeTelemetry
                        .InterruptibilityReason,
                    Fingerprint = projection.RuntimeTelemetry.Fingerprint,
                    Metrics = projection.RuntimeTelemetry.Metrics
                        .Select(metric => new ControlCenterStageCWireMetric
                        {
                            Id = metric.Id,
                            Group = metric.Group,
                            Label = metric.Label,
                            Value = metric.Value,
                            Unit = metric.Unit,
                            Health = (int)metric.Health
                        })
                        .ToArray()
                }
        };
}

internal static class ControlCenterStageCWireCodec
{
    internal const int MaximumFrameBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    internal static byte[] EncodeRequest(ControlCenterStageCRequest request)
        => Encode(request ?? throw new ArgumentNullException(nameof(request)));

    internal static byte[] EncodeResponse(ControlCenterStageCResponse response)
        => Encode(response ?? throw new ArgumentNullException(nameof(response)));

    internal static ControlCenterStageCRequest DecodeRequest(byte[] frame)
        => Decode<ControlCenterStageCRequest>(frame);

    internal static ControlCenterStageCResponse DecodeResponse(byte[] frame)
        => Decode<ControlCenterStageCResponse>(frame);

    internal static async Task WriteFrameAsync(
        Stream stream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        ValidateFrame(frame);
        var prefix = BitConverter.GetBytes(frame.Length);
        await stream.WriteAsync(
            prefix.AsMemory(0, prefix.Length),
            cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(
            frame.AsMemory(0, frame.Length),
            cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(
            stream,
            prefix,
            cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(prefix, 0);
        if (length <= 0 || length > MaximumFrameBytes)
            throw new InvalidDataException("Control Center frame rejected.");
        var frame = new byte[length];
        await ReadExactlyAsync(
            stream,
            frame,
            cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static byte[] Encode<T>(T value)
    {
        var frame = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        ValidateFrame(frame);
        return frame;
    }

    private static T Decode<T>(byte[] frame)
    {
        ValidateFrame(frame);
        var value = JsonSerializer.Deserialize<T>(frame, JsonOptions);
        return value ?? throw new InvalidDataException(
            "Control Center frame was empty.");
    }

    private static void ValidateFrame(byte[]? frame)
    {
        if (frame == null
            || frame.Length == 0
            || frame.Length > MaximumFrameBytes)
        {
            throw new InvalidDataException("Control Center frame rejected.");
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
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }
}

internal static class ControlCenterStageCNamedPipe
{
    internal const string StablePipeName = "lilith-control-center-v1";
    internal const int MaximumServerInstances = 1;

    internal static NamedPipeServerStream CreateServer(string pipeName)
        => new(
            ValidatePipeName(pipeName),
            PipeDirection.InOut,
            MaximumServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    internal static NamedPipeClientStream CreateClient(string pipeName)
        => new(
            ".",
            ValidatePipeName(pipeName),
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    internal static async Task RunSingleSessionAsync(
        string pipeName,
        Func<LegacyControlCenterProjection?> projectionSource,
        CancellationToken cancellationToken)
    {
        if (projectionSource == null)
            throw new ArgumentNullException(nameof(projectionSource));
        using var server = CreateServer(pipeName);
        await server.WaitForConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        var session = new ControlCenterStageCSession();
        for (var count = 0;
             count < ControlCenterStageCSession.MaximumRequests
                 && server.IsConnected;
             count++)
        {
            var request = ControlCenterStageCWireCodec.DecodeRequest(
                await ControlCenterStageCWireCodec.ReadFrameAsync(
                    server,
                    cancellationToken).ConfigureAwait(false));
            var response = session.Handle(
                request,
                projectionSource(),
                DateTimeOffset.UtcNow);
            await ControlCenterStageCWireCodec.WriteFrameAsync(
                server,
                ControlCenterStageCWireCodec.EncodeResponse(response),
                cancellationToken).ConfigureAwait(false);
            if ((ControlCenterTransportAdmission)response.Admission
                == ControlCenterTransportAdmission.AcceptedAndClosed)
            {
                break;
            }
        }
    }

    internal static async Task<ControlCenterStageCResponse> SendAsync(
        NamedPipeClientStream client,
        ControlCenterStageCRequest request,
        CancellationToken cancellationToken)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (!client.IsConnected)
            throw new InvalidOperationException("Client is not connected.");
        await ControlCenterStageCWireCodec.WriteFrameAsync(
            client,
            ControlCenterStageCWireCodec.EncodeRequest(request),
            cancellationToken).ConfigureAwait(false);
        return ControlCenterStageCWireCodec.DecodeResponse(
            await ControlCenterStageCWireCodec.ReadFrameAsync(
                client,
                cancellationToken).ConfigureAwait(false));
    }

    private static string ValidatePipeName(string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName)
            || pipeName.Length > 128
            || !pipeName.StartsWith(
                "lilith-control-center-",
                StringComparison.Ordinal)
            || pipeName.Any(
                character => !char.IsLetterOrDigit(character)
                    && character != '-'
                    && character != '_'))
        {
            throw new ArgumentException(
                "A bounded Lilith Control Center pipe name is required.",
                nameof(pipeName));
        }
        return pipeName;
    }
}

internal readonly struct ControlCenterStageCAuthorityCounters
{
    internal int SettingsWrites { get; }
    internal int ConfigurationWrites { get; }
    internal int CredentialReads { get; }
    internal int ProviderCalls { get; }
    internal int PromptWrites { get; }
    internal int MemoryWrites { get; }
    internal int DecisionOutputs { get; }
    internal int QueueWrites { get; }
    internal int MotionOutputs { get; }
    internal int ExecutionOutputs { get; }
    internal int PlayerDataWrites { get; }
    internal int DesktopOutputs { get; }

    internal static ControlCenterStageCAuthorityCounters Zero => default;
}
