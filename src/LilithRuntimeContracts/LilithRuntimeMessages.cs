using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

public enum LilithRuntimeMessageKind
{
    RuntimeHello = 1,
    RuntimeStatus = 2,
    HermesEvent = 3,
    RuntimeRequest = 4,
    RuntimeResponse = 5
}

public enum LilithRuntimeState
{
    Starting = 1,
    Ready = 2,
    Degraded = 3,
    Stopping = 4,
    Stopped = 5,
    Faulted = 6
}

public abstract class LilithRuntimeMessage
{
    public abstract LilithRuntimeMessageKind Kind { get; }

    internal abstract void WriteBody(Utf8JsonWriter writer);
}

public sealed class LilithRuntimeHello : LilithRuntimeMessage
{
    private readonly IReadOnlyList<string> _capabilities;

    public override LilithRuntimeMessageKind Kind =>
        LilithRuntimeMessageKind.RuntimeHello;

    public string RuntimeVersion { get; }
    public string HermesVersion { get; }
    public string HermesCommit { get; }
    public IReadOnlyList<string> Capabilities => _capabilities;
    public bool Ready { get; }

    public LilithRuntimeHello(
        string runtimeVersion,
        string hermesVersion,
        string hermesCommit,
        IEnumerable<string> capabilities,
        bool ready)
    {
        RuntimeVersion = LilithRuntimeProtocolValidation.Text(
            runtimeVersion,
            LilithRuntimeProtocol.MaximumVersionUtf8Bytes,
            nameof(runtimeVersion));
        HermesVersion = LilithRuntimeProtocolValidation.Text(
            hermesVersion,
            LilithRuntimeProtocol.MaximumVersionUtf8Bytes,
            nameof(hermesVersion));
        HermesCommit = LilithRuntimeProtocolValidation.HermesCommit(
            hermesCommit,
            nameof(hermesCommit));
        if (capabilities is null)
            throw new ArgumentNullException(nameof(capabilities));

        var capabilityValues = capabilities.ToArray();
        if (capabilityValues.Length > LilithRuntimeProtocol.MaximumCapabilities)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capabilities),
                "Too many Runtime capabilities were supplied.");
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < capabilityValues.Length; index++)
        {
            capabilityValues[index] = LilithRuntimeProtocolValidation.Token(
                capabilityValues[index],
                LilithRuntimeProtocol.MaximumCapabilityUtf8Bytes,
                $"{nameof(capabilities)}[{index}]");
            if (!distinct.Add(capabilityValues[index]))
                throw new ArgumentException("Runtime capabilities must be unique.", nameof(capabilities));
        }

        _capabilities = Array.AsReadOnly(capabilityValues);
        Ready = ready;
    }

    internal override void WriteBody(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("runtimeVersion", RuntimeVersion);
        writer.WriteString("hermesVersion", HermesVersion);
        writer.WriteString("hermesCommit", HermesCommit);
        writer.WritePropertyName("capabilities");
        writer.WriteStartArray();
        foreach (var capability in _capabilities)
            writer.WriteStringValue(capability);
        writer.WriteEndArray();
        writer.WriteBoolean("ready", Ready);
        writer.WriteEndObject();
    }
}

public sealed class LilithRuntimeStatus : LilithRuntimeMessage
{
    public override LilithRuntimeMessageKind Kind =>
        LilithRuntimeMessageKind.RuntimeStatus;

    public LilithRuntimeState State { get; }
    public string Code { get; }
    public string Message { get; }
    public string? ActiveSessionId { get; }

    public LilithRuntimeStatus(
        LilithRuntimeState state,
        string code,
        string message,
        string? activeSessionId)
    {
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        State = state;
        Code = LilithRuntimeProtocolValidation.Token(
            code,
            LilithRuntimeProtocol.MaximumStatusCodeUtf8Bytes,
            nameof(code));
        Message = LilithRuntimeProtocolValidation.Text(
            message,
            LilithRuntimeProtocol.MaximumStatusMessageUtf8Bytes,
            nameof(message),
            allowEmpty: true);
        ActiveSessionId = activeSessionId is null
            ? null
            : LilithRuntimeProtocolValidation.Text(
                activeSessionId,
                LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes,
                nameof(activeSessionId));
    }

    internal override void WriteBody(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("state", LilithRuntimeProtocol.StateToken(State));
        writer.WriteString("code", Code);
        writer.WriteString("message", Message);
        if (ActiveSessionId is null)
            writer.WriteNull("activeSessionId");
        else
            writer.WriteString("activeSessionId", ActiveSessionId);
        writer.WriteEndObject();
    }
}

public sealed class LilithRuntimeHermesEvent : LilithRuntimeMessage
{
    private readonly JsonElement _hermesPayload;

    public override LilithRuntimeMessageKind Kind =>
        LilithRuntimeMessageKind.HermesEvent;

    public string Method { get; }
    public JsonElement HermesPayload => _hermesPayload;

    public LilithRuntimeHermesEvent(string method, JsonElement hermesPayload)
    {
        Method = LilithRuntimeProtocolValidation.Method(method, nameof(method));
        _hermesPayload = LilithRuntimeProtocolValidation.JsonPayload(
            hermesPayload,
            nameof(hermesPayload));
    }

    internal override void WriteBody(Utf8JsonWriter writer)
        => LilithRuntimeProtocol.WriteHermesBody(writer, Method, _hermesPayload);
}

public sealed class LilithRuntimeRequest : LilithRuntimeMessage
{
    private readonly JsonElement _hermesPayload;

    public override LilithRuntimeMessageKind Kind =>
        LilithRuntimeMessageKind.RuntimeRequest;

    public string Method { get; }
    public JsonElement HermesPayload => _hermesPayload;

    public LilithRuntimeRequest(string method, JsonElement hermesPayload)
    {
        Method = LilithRuntimeProtocolValidation.Method(method, nameof(method));
        _hermesPayload = LilithRuntimeProtocolValidation.JsonPayload(
            hermesPayload,
            nameof(hermesPayload));
    }

    internal override void WriteBody(Utf8JsonWriter writer)
        => LilithRuntimeProtocol.WriteHermesBody(writer, Method, _hermesPayload);
}

public sealed class LilithRuntimeResponse : LilithRuntimeMessage
{
    private readonly JsonElement _hermesPayload;

    public override LilithRuntimeMessageKind Kind =>
        LilithRuntimeMessageKind.RuntimeResponse;

    public string Method { get; }
    public bool Succeeded { get; }
    public JsonElement HermesPayload => _hermesPayload;

    public LilithRuntimeResponse(
        string method,
        bool succeeded,
        JsonElement hermesPayload)
    {
        Method = LilithRuntimeProtocolValidation.Method(method, nameof(method));
        Succeeded = succeeded;
        _hermesPayload = LilithRuntimeProtocolValidation.JsonPayload(
            hermesPayload,
            nameof(hermesPayload));
    }

    internal override void WriteBody(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("method", Method);
        writer.WriteBoolean("succeeded", Succeeded);
        writer.WritePropertyName("hermesPayload");
        writer.WriteRawValue(_hermesPayload.GetRawText());
        writer.WriteEndObject();
    }
}

public sealed class LilithRuntimeEnvelope
{
    public int ProtocolVersion => LilithRuntimeProtocol.Version;
    public Guid GenerationId { get; }
    public Guid RequestId { get; }
    public long Sequence { get; }
    public long SentAtUnixTimeMilliseconds { get; }
    public LilithRuntimeMessage Message { get; }
    public LilithRuntimeMessageKind Kind => Message.Kind;

    public LilithRuntimeEnvelope(
        Guid generationId,
        Guid requestId,
        long sequence,
        long sentAtUnixTimeMilliseconds,
        LilithRuntimeMessage message)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("Runtime generation ID must not be empty.", nameof(generationId));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Runtime request ID must not be empty.", nameof(requestId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (sentAtUnixTimeMilliseconds is < 0
            or > LilithRuntimeProtocol.MaximumUnixTimeMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(sentAtUnixTimeMilliseconds));
        }

        GenerationId = generationId;
        RequestId = requestId;
        Sequence = sequence;
        SentAtUnixTimeMilliseconds = sentAtUnixTimeMilliseconds;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}

internal static class LilithRuntimeProtocolValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string Text(
        string? value,
        int maximumUtf8Bytes,
        string parameterName,
        bool allowEmpty = false)
    {
        if (value is null
            || (!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Protocol text was rejected.", parameterName);
        }

        int utf8Bytes;
        try
        {
            utf8Bytes = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Protocol text was not valid Unicode.", parameterName, exception);
        }

        if (utf8Bytes > maximumUtf8Bytes)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static string Token(
        string? value,
        int maximumUtf8Bytes,
        string parameterName)
    {
        value = Text(value, maximumUtf8Bytes, parameterName);
        if (value.Any(character =>
                !((character >= 'a' && character <= 'z')
                  || (character >= 'A' && character <= 'Z')
                  || (character >= '0' && character <= '9')
                  || character is '.' or '_' or '-' or '/' or ':')))
        {
            throw new ArgumentException("Protocol token was rejected.", parameterName);
        }
        return value;
    }

    internal static string Method(string? value, string parameterName)
        => Token(value, LilithRuntimeProtocol.MaximumMethodUtf8Bytes, parameterName);

    internal static string HermesCommit(string? value, string parameterName)
    {
        value = Text(value, 40, parameterName);
        if (value.Length != 40
            || value.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Hermes commit must be a canonical lowercase SHA-1 identity.",
                parameterName);
        }
        return value;
    }

    internal static JsonElement JsonPayload(JsonElement value, string parameterName)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Hermes JSON payload was undefined.", parameterName);
        if (StrictUtf8.GetByteCount(value.GetRawText())
            > LilithRuntimeProtocol.MaximumHermesPayloadUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        if (JsonDepth(value) > LilithRuntimeProtocol.MaximumHermesPayloadJsonDepth)
            throw new ArgumentOutOfRangeException(parameterName);
        return value.Clone();
    }

    private static int JsonDepth(JsonElement value)
    {
        var childMaximum = 0;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                childMaximum = Math.Max(childMaximum, JsonDepth(property.Value));
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                childMaximum = Math.Max(childMaximum, JsonDepth(item));
        }
        return childMaximum + 1;
    }
}
