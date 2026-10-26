using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

public enum LilithNoteInboxAction { Deliver = 1, Status = 2 }
public enum LilithNoteInboxStatus { Delivered = 1, Deferred = 2, Rejected = 3, Failed = 4, Unknown = 5, NotFound = 6 }

public sealed class LilithNoteInboxCommand
{
    public Guid OperationId { get; }
    public Guid DeliveryId { get; }
    public string OriginTurnId { get; }
    public Guid ClientTurnId { get; }
    public LilithNoteInboxAction Action { get; }
    public string? Text { get; }
    public string Locale { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public Guid RuntimeGeneration { get; }

    public LilithNoteInboxCommand(Guid operationId, Guid deliveryId, string originTurnId,
        Guid clientTurnId, LilithNoteInboxAction action, string? text, string locale,
        DateTimeOffset expiresAtUtc, Guid runtimeGeneration)
    {
        LilithNoteInboxProtocol.RequireId(operationId);
        LilithNoteInboxProtocol.RequireId(deliveryId);
        _ = LilithNoteInboxProtocol.ParseId(originTurnId);
        LilithNoteInboxProtocol.RequireId(clientTurnId);
        LilithNoteInboxProtocol.RequireId(runtimeGeneration);
        if (!Enum.IsDefined(typeof(LilithNoteInboxAction), action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (locale is not ("zh-TW" or "zh-CN" or "ja" or "en")) throw new ArgumentException("Note locale rejected.");
        if (action == LilithNoteInboxAction.Deliver) LilithNoteInboxProtocol.ValidateText(text);
        else if (text is not null) throw new ArgumentException("Status cannot contain note text.");
        LilithNoteInboxProtocol.RequireUtc(expiresAtUtc);
        OperationId = operationId; DeliveryId = deliveryId; OriginTurnId = originTurnId;
        ClientTurnId = clientTurnId; Action = action; Text = text; Locale = locale;
        ExpiresAtUtc = expiresAtUtc; RuntimeGeneration = runtimeGeneration;
    }
}

public sealed class LilithNoteInboxArtifact
{
    public Guid ArtifactId { get; }
    public string Sha256 { get; }
    public long ByteLength { get; }
    public DateTimeOffset CompletedAtUtc { get; }
    public LilithNoteInboxArtifact(Guid artifactId, string sha256, long byteLength, DateTimeOffset completedAtUtc)
    {
        LilithNoteInboxProtocol.RequireId(artifactId);
        if (sha256 is not { Length: 64 } || sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new ArgumentException("Exact uppercase artifact digest required.");
        if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        LilithNoteInboxProtocol.RequireUtc(completedAtUtc);
        ArtifactId = artifactId; Sha256 = sha256; ByteLength = byteLength; CompletedAtUtc = completedAtUtc;
    }
}

public sealed class LilithNoteInboxResult
{
    public Guid OperationId { get; }
    public Guid DeliveryId { get; }
    public LilithNoteInboxStatus Status { get; }
    public string Code { get; }
    public LilithNoteInboxArtifact? Artifact { get; }
    public LilithNoteInboxResult(Guid operationId, Guid deliveryId, LilithNoteInboxStatus status,
        string code, LilithNoteInboxArtifact? artifact = null)
    {
        LilithNoteInboxProtocol.RequireId(operationId); LilithNoteInboxProtocol.RequireId(deliveryId);
        if (!Enum.IsDefined(typeof(LilithNoteInboxStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (string.IsNullOrEmpty(code) || code.Length > 96 || code[0] is not (>= 'a' and <= 'z') || code.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not ('.' or '_')))
            throw new ArgumentException("Note result code rejected.");
        var delivered = status == LilithNoteInboxStatus.Delivered;
        if (delivered != (code == "ok") || delivered != (artifact is not null)
            || artifact is not null && artifact.ArtifactId != deliveryId)
            throw new ArgumentException("Only an exact game artifact may prove delivery.");
        OperationId = operationId; DeliveryId = deliveryId; Status = status; Code = code; Artifact = artifact;
    }
}

public static class LilithNoteInboxProtocol
{
    public const int Version = 1;
    public const int MaximumTextCharacters = 1200;
    public const string CommandKind = "lilith.noteinbox.command";
    public const string ResultKind = "lilith.noteinbox.result";
    public const string HostRequestKind = "lilith.noteinbox.host-request";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Guid DeriveDeliveryId(string sessionId, string originTurnId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.IndexOf('\0') >= 0 || StrictUtf8.GetByteCount(sessionId) > 240)
            throw new ArgumentException("Exact session identity required.");
        _ = ParseId(originTurnId);
        var digest = SHA256.HashData(StrictUtf8.GetBytes("lilith.noteinbox.delivery.v1\0" + sessionId + "\0" + originTurnId));
        return Guid.ParseExact(Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant(), "N");
    }

    public static LilithNoteInboxCommand DecodeArguments(JsonElement value, Guid operationId,
        Guid deliveryId, string originTurnId, Guid clientTurnId, DateTimeOffset expiresAtUtc, Guid generation)
    {
        var action = String(value, "action") switch
        {
            "deliver" => LilithNoteInboxAction.Deliver, "status" => LilithNoteInboxAction.Status,
            _ => throw new InvalidDataException("Note action rejected.")
        };
        Exact(value, action == LilithNoteInboxAction.Deliver ? new[] { "action", "text", "locale" } : new[] { "action", "locale" });
        return new(operationId, deliveryId, originTurnId, clientTurnId, action,
            action == LilithNoteInboxAction.Deliver ? String(value, "text") : null,
            String(value, "locale"), expiresAtUtc, generation);
    }

    public static JsonElement EncodeCommand(LilithNoteInboxCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return JsonSerializer.SerializeToElement(new
        {
            version = Version, kind = CommandKind, operation_id = command.OperationId.ToString("N"),
            delivery_id = command.DeliveryId.ToString("N"), origin_turn_id = command.OriginTurnId,
            client_turn_id = command.ClientTurnId.ToString("N"), action = command.Action == LilithNoteInboxAction.Deliver ? "deliver" : "status",
            text = command.Text, locale = command.Locale, expires_at_utc = command.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
            runtime_generation = command.RuntimeGeneration.ToString("N")
        });
    }

    public static LilithNoteInboxCommand ReadCommand(JsonElement value)
    {
        Exact(value, "version", "kind", "operation_id", "delivery_id", "origin_turn_id", "client_turn_id", "action", "text", "locale", "expires_at_utc", "runtime_generation");
        Header(value, CommandKind);
        var action = String(value, "action") switch
        {
            "deliver" => LilithNoteInboxAction.Deliver, "status" => LilithNoteInboxAction.Status,
            _ => throw new InvalidDataException("Note action rejected.")
        };
        var text = value.GetProperty("text");
        return new(ParseId(String(value, "operation_id")), ParseId(String(value, "delivery_id")),
            String(value, "origin_turn_id"), ParseId(String(value, "client_turn_id")), action,
            text.ValueKind == JsonValueKind.Null ? null : String(value, "text"), String(value, "locale"),
            ParseUtc(String(value, "expires_at_utc")), ParseId(String(value, "runtime_generation")));
    }

    public static JsonElement EncodeResult(LilithNoteInboxResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.SerializeToElement(new
        {
            version = Version, kind = ResultKind, operation_id = result.OperationId.ToString("N"),
            delivery_id = result.DeliveryId.ToString("N"), status = StatusToken(result.Status), code = result.Code,
            artifact = result.Artifact is null ? null : new
            {
                artifact_id = result.Artifact.ArtifactId.ToString("N"), sha256 = result.Artifact.Sha256,
                byte_length = result.Artifact.ByteLength,
                completed_at_utc = result.Artifact.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            }
        });
    }

    public static LilithNoteInboxResult ReadResult(JsonElement value)
    {
        Exact(value, "version", "kind", "operation_id", "delivery_id", "status", "code", "artifact");
        Header(value, ResultKind);
        LilithNoteInboxArtifact? artifact = null;
        var item = value.GetProperty("artifact");
        if (item.ValueKind != JsonValueKind.Null)
        {
            Exact(item, "artifact_id", "sha256", "byte_length", "completed_at_utc");
            artifact = new(ParseId(String(item, "artifact_id")), String(item, "sha256"),
                item.GetProperty("byte_length").GetInt64(), ParseUtc(String(item, "completed_at_utc")));
        }
        return new(ParseId(String(value, "operation_id")), ParseId(String(value, "delivery_id")),
            ParseStatus(String(value, "status")), String(value, "code"), artifact);
    }

    public static string StatusToken(LilithNoteInboxStatus status) => status switch
    {
        LilithNoteInboxStatus.Delivered => "delivered", LilithNoteInboxStatus.Deferred => "deferred",
        LilithNoteInboxStatus.Rejected => "rejected", LilithNoteInboxStatus.Failed => "failed",
        LilithNoteInboxStatus.Unknown => "unknown", LilithNoteInboxStatus.NotFound => "not_found",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
    public static LilithNoteInboxStatus ParseStatus(string status) => status switch
    {
        "delivered" => LilithNoteInboxStatus.Delivered, "deferred" => LilithNoteInboxStatus.Deferred,
        "rejected" => LilithNoteInboxStatus.Rejected, "failed" => LilithNoteInboxStatus.Failed,
        "unknown" => LilithNoteInboxStatus.Unknown, "not_found" => LilithNoteInboxStatus.NotFound,
        _ => throw new InvalidDataException("Note status rejected.")
    };
    public static void Exact(JsonElement value, params string[] properties)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Note object required.");
        var expected = new HashSet<string>(properties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!expected.Contains(property.Name) || !seen.Add(property.Name)) throw new InvalidDataException("Note property rejected.");
        if (!seen.SetEquals(expected)) throw new InvalidDataException("Note property missing.");
    }
    public static string String(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var field) || field.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Note string required.");
        return field.GetString()!;
    }
    public static Guid ParseId(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var id) || id == Guid.Empty || id.ToString("N") != value)
            throw new InvalidDataException("Canonical note identity required.");
        return id;
    }
    internal static void RequireId(Guid id) { if (id == Guid.Empty) throw new ArgumentException("Note identity required."); }
    internal static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue)
            throw new ArgumentException("A finite UTC note timestamp is required.");
    }
    private static DateTimeOffset ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new InvalidDataException("Canonical note timestamp required.");
        RequireUtc(parsed); return parsed;
    }
    internal static void ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextCharacters
            || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > MaximumTextCharacters * 4)
            throw new ArgumentException("Note text rejected.");
    }
    private static void Header(JsonElement value, string kind)
    {
        if (value.GetProperty("version").GetInt32() != Version || String(value, "kind") != kind)
            throw new InvalidDataException("Note envelope rejected.");
    }
}
