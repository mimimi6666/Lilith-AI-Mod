using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LilithGameBridgeContracts;

// Content-free identity of one completed, synthetic assistant turn. This is
// presentation correlation, never permission to replay a history row.
public sealed class LilithGameBridgeContinuationContext
{
    public string EventId { get; }
    public string TurnId { get; }
    public string MessageId { get; }
    public long CompletedAtUnixMilliseconds { get; }
    public long ExpiresAtUnixMilliseconds { get; }
    public bool Retired { get; }
    public const int LifetimeMilliseconds = 90_000;

    public LilithGameBridgeContinuationContext(string eventId, string turnId, string messageId,
        long completedAtUnixMilliseconds, long expiresAtUnixMilliseconds)
        : this(eventId, turnId, messageId, completedAtUnixMilliseconds, expiresAtUnixMilliseconds, false)
    {
    }

    public LilithGameBridgeContinuationContext(string eventId, string turnId, string messageId,
        long completedAtUnixMilliseconds, long expiresAtUnixMilliseconds, bool retired)
    {
        foreach (var id in new[] { eventId, turnId, messageId })
            if (id is null || !Regex.IsMatch(id, "\\A[a-f0-9]{32}\\z") || id == new string('0', 32))
                throw new ArgumentException("Synthetic continuation identity is invalid.");
        if (eventId == turnId || eventId == messageId || turnId == messageId)
            throw new ArgumentException("Synthetic continuation identities must be distinct.");
        if (completedAtUnixMilliseconds <= 0 || expiresAtUnixMilliseconds > 253402300799999
            || expiresAtUnixMilliseconds <= completedAtUnixMilliseconds
            || expiresAtUnixMilliseconds - completedAtUnixMilliseconds > LifetimeMilliseconds)
            throw new ArgumentException("Synthetic continuation lifetime is invalid.");
        EventId = eventId;
        TurnId = turnId;
        MessageId = messageId;
        CompletedAtUnixMilliseconds = completedAtUnixMilliseconds;
        ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds;
        Retired = retired;
    }

    public bool IsCurrent(long nowUnixMilliseconds)
        => !Retired && nowUnixMilliseconds >= CompletedAtUnixMilliseconds && nowUnixMilliseconds <= ExpiresAtUnixMilliseconds;

    public LilithGameBridgeContinuationContext Retire()
        => new(EventId, TurnId, MessageId, CompletedAtUnixMilliseconds, ExpiresAtUnixMilliseconds, true);

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("continuation");
        writer.WriteString("eventId", EventId);
        writer.WriteString("turnId", TurnId);
        writer.WriteString("messageId", MessageId);
        writer.WriteNumber("completedAtUnixMs", CompletedAtUnixMilliseconds);
        writer.WriteNumber("expiresAtUnixMs", ExpiresAtUnixMilliseconds);
        writer.WriteBoolean("retired", Retired);
        writer.WriteEndObject();
    }

    internal static LilithGameBridgeContinuationContext Read(JsonElement value)
    {
        var hasRetired = value.TryGetProperty("retired", out var retired);
        RequireFields(value, hasRetired
            ? new[] { "eventId", "turnId", "messageId", "completedAtUnixMs", "expiresAtUnixMs", "retired" }
            : new[] { "eventId", "turnId", "messageId", "completedAtUnixMs", "expiresAtUnixMs" });
        return new(value.GetProperty("eventId").GetString()!, value.GetProperty("turnId").GetString()!,
            value.GetProperty("messageId").GetString()!, value.GetProperty("completedAtUnixMs").GetInt64(),
            value.GetProperty("expiresAtUnixMs").GetInt64(), hasRetired && retired.GetBoolean());
    }

    internal static void RequireFields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a closed object.");
        var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!remaining.Remove(field.Name)) throw new ArgumentException("Unexpected or repeated field.");
        if (remaining.Count != 0) throw new ArgumentException("Missing field.");
    }
}
