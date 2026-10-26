using System.Buffers.Binary;
using System.Text.Json;

namespace LilithRuntimeContracts;

public static class LilithRuntimeFrameCodec
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = LilithRuntimeProtocol.MaximumJsonDepth
    };

    public static byte[] Encode(LilithRuntimeEnvelope envelope)
    {
        if (envelope is null)
            throw new ArgumentNullException(nameof(envelope));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", envelope.ProtocolVersion);
            writer.WriteString("kind", LilithRuntimeProtocol.KindToken(envelope.Kind));
            writer.WriteString("generationId", envelope.GenerationId.ToString("N"));
            writer.WriteString("requestId", envelope.RequestId.ToString("N"));
            writer.WriteNumber("sequence", envelope.Sequence);
            writer.WriteNumber(
                "sentAtUnixTimeMilliseconds",
                envelope.SentAtUnixTimeMilliseconds);
            writer.WritePropertyName("body");
            envelope.Message.WriteBody(writer);
            writer.WriteEndObject();
        }

        if (stream.Length is <= 0 or > LilithRuntimeProtocol.MaximumFrameBytes)
            throw new InvalidDataException("Runtime frame length was rejected.");
        return stream.ToArray();
    }

    public static LilithRuntimeEnvelope Decode(ReadOnlyMemory<byte> frame)
    {
        ValidateFrame(frame);
        try
        {
            using var document = JsonDocument.Parse(frame, DocumentOptions);
            var root = ExactObject(
                document.RootElement,
                "version",
                "kind",
                "generationId",
                "requestId",
                "sequence",
                "sentAtUnixTimeMilliseconds",
                "body");

            var version = RequiredInt32(root, "version");
            if (version != LilithRuntimeProtocol.Version)
                throw new InvalidDataException("Runtime protocol version was rejected.");

            var kind = LilithRuntimeProtocol.ParseKindToken(
                RequiredString(root, "kind"));
            var generationId = RequiredGuid(root, "generationId");
            var requestId = RequiredGuid(root, "requestId");
            var sequence = RequiredInt64(root, "sequence");
            var sentAt = RequiredInt64(root, "sentAtUnixTimeMilliseconds");
            var message = DecodeMessage(kind, root["body"]);
            return new LilithRuntimeEnvelope(
                generationId,
                requestId,
                sequence,
                sentAt,
                message);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Runtime frame JSON was rejected.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Runtime frame shape was rejected.", exception);
        }
    }

    public static async Task WriteAsync(
        Stream stream,
        LilithRuntimeEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite)
            throw new ArgumentException("Runtime stream must be writable.", nameof(stream));

        var frame = Encode(envelope);
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, frame.Length);
        await stream.WriteAsync(prefix.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<LilithRuntimeEnvelope> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException("Runtime stream must be readable.", nameof(stream));

        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        var frameLength = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (frameLength is <= 0 or > LilithRuntimeProtocol.MaximumFrameBytes)
            throw new InvalidDataException("Runtime frame prefix was rejected.");

        var frame = new byte[frameLength];
        await ReadExactlyAsync(stream, frame, cancellationToken).ConfigureAwait(false);
        return Decode(frame);
    }

    private static LilithRuntimeMessage DecodeMessage(
        LilithRuntimeMessageKind kind,
        JsonElement body)
        => kind switch
        {
            LilithRuntimeMessageKind.RuntimeHello => DecodeHello(body),
            LilithRuntimeMessageKind.RuntimeStatus => DecodeStatus(body),
            LilithRuntimeMessageKind.HermesEvent => DecodeHermesEvent(body),
            LilithRuntimeMessageKind.RuntimeRequest => DecodeRequest(body),
            LilithRuntimeMessageKind.RuntimeResponse => DecodeResponse(body),
            _ => throw new InvalidDataException("Runtime message kind was rejected.")
        };

    private static LilithRuntimeHello DecodeHello(JsonElement body)
    {
        var values = ExactObject(
            body,
            "runtimeVersion",
            "hermesVersion",
            "hermesCommit",
            "capabilities",
            "ready");
        var capabilitiesElement = values["capabilities"];
        if (capabilitiesElement.ValueKind != JsonValueKind.Array
            || capabilitiesElement.GetArrayLength()
                > LilithRuntimeProtocol.MaximumCapabilities)
        {
            throw new InvalidDataException("Runtime capabilities were rejected.");
        }

        var capabilities = new List<string>(capabilitiesElement.GetArrayLength());
        foreach (var capability in capabilitiesElement.EnumerateArray())
        {
            if (capability.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Runtime capability was rejected.");
            capabilities.Add(capability.GetString()!);
        }

        return new LilithRuntimeHello(
            RequiredString(values, "runtimeVersion"),
            RequiredString(values, "hermesVersion"),
            RequiredString(values, "hermesCommit"),
            capabilities,
            RequiredBoolean(values, "ready"));
    }

    private static LilithRuntimeStatus DecodeStatus(JsonElement body)
    {
        var values = ExactObject(
            body,
            "state",
            "code",
            "message",
            "activeSessionId");
        var session = values["activeSessionId"];
        if (session.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            throw new InvalidDataException("Runtime active session was rejected.");
        return new LilithRuntimeStatus(
            LilithRuntimeProtocol.ParseStateToken(RequiredString(values, "state")),
            RequiredString(values, "code"),
            RequiredString(values, "message"),
            session.ValueKind == JsonValueKind.Null ? null : session.GetString());
    }

    private static LilithRuntimeHermesEvent DecodeHermesEvent(JsonElement body)
    {
        var values = ExactObject(body, "method", "hermesPayload");
        return new LilithRuntimeHermesEvent(
            RequiredString(values, "method"),
            values["hermesPayload"]);
    }

    private static LilithRuntimeRequest DecodeRequest(JsonElement body)
    {
        var values = ExactObject(body, "method", "hermesPayload");
        return new LilithRuntimeRequest(
            RequiredString(values, "method"),
            values["hermesPayload"]);
    }

    private static LilithRuntimeResponse DecodeResponse(JsonElement body)
    {
        var values = ExactObject(body, "method", "succeeded", "hermesPayload");
        return new LilithRuntimeResponse(
            RequiredString(values, "method"),
            RequiredBoolean(values, "succeeded"),
            values["hermesPayload"]);
    }

    private static Dictionary<string, JsonElement> ExactObject(
        JsonElement element,
        params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Runtime JSON object was rejected.");

        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException("Runtime JSON property was rejected.");
            }
        }

        if (values.Count != expected.Count)
            throw new InvalidDataException("Runtime JSON property was missing.");
        return values;
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Runtime JSON string was rejected.");
        return value.GetString()!;
    }

    private static int RequiredInt32(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException("Runtime JSON integer was rejected.");
        }
        return result;
    }

    private static long RequiredInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw new InvalidDataException("Runtime JSON integer was rejected.");
        }
        return result;
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Runtime JSON boolean was rejected.");
        return value.GetBoolean();
    }

    private static Guid RequiredGuid(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var text = RequiredString(values, name);
        if (!Guid.TryParseExact(text, "N", out var result)
            || result == Guid.Empty)
        {
            throw new InvalidDataException("Runtime identity was rejected.");
        }
        return result;
    }

    private static void ValidateFrame(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length is <= 0 or > LilithRuntimeProtocol.MaximumFrameBytes)
            throw new InvalidDataException("Runtime frame length was rejected.");
        var span = frame.Span;
        if (span.Length >= 3
            && span[0] == 0xEF
            && span[1] == 0xBB
            && span[2] == 0xBF)
        {
            throw new InvalidDataException("Runtime frame byte-order mark was rejected.");
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
                throw new EndOfStreamException("Runtime frame ended early.");
            offset += read;
        }
    }
}
