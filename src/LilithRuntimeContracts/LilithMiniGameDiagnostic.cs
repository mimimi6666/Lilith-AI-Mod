using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LilithGameBridgeContracts;

// Content-free, opt-in request-local observation. This is neither game state nor
// authority to call a model. Ordinary requests omit it completely.
public sealed class LilithMiniGameDiagnosticRequest
{
    public const long MaximumLifetimeMilliseconds = 30 * 60 * 1000;
    public Guid TraceId { get; }
    public long ExpiresAtUnixMs { get; }

    public LilithMiniGameDiagnosticRequest(Guid traceId, long expiresAtUnixMs)
    {
        if (traceId == Guid.Empty || expiresAtUnixMs <= 0)
            throw new ArgumentException("Mini-game diagnostic lease rejected.");
        TraceId = traceId;
        ExpiresAtUnixMs = expiresAtUnixMs;
    }

    public bool IsActive(long nowUnixMs) => nowUnixMs >= 0 && ExpiresAtUnixMs > nowUnixMs
        && ExpiresAtUnixMs - nowUnixMs <= MaximumLifetimeMilliseconds;

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("traceId", TraceId.ToString("N"));
        writer.WriteNumber("expiresAtUnixMs", ExpiresAtUnixMs);
        writer.WriteEndObject();
    }

    public JsonElement ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static LilithMiniGameDiagnosticRequest Parse(JsonElement value)
    {
        LilithMiniGameDiagnosticReceipt.RequireExact(value, "traceId", "expiresAtUnixMs");
        if (!value.GetProperty("expiresAtUnixMs").TryGetInt64(out var expiry))
            throw new InvalidDataException("Mini-game diagnostic lease rejected.");
        return new LilithMiniGameDiagnosticRequest(
            LilithMiniGameDiagnosticReceipt.ReadTraceId(value), expiry);
    }
}

// All non-identity values are a closed vocabulary. No arbitrary error, route
// name, message, card, square, prompt or model response can enter this receipt.
public sealed class LilithMiniGameDiagnosticReceipt
{
    public Guid TraceId { get; }
    public string RuntimeReceived { get; }
    public string RuntimeForwarded { get; }
    public string NativeIngress { get; }
    public string NativeAgent { get; }
    public string NativeDispatch { get; }
    public string Route { get; }
    public bool? StrictPolicyEnabled { get; }
    public LilithMiniGameWireDiagnostic? Wire { get; }

    public LilithMiniGameDiagnosticReceipt(Guid traceId, string runtimeReceived,
        string runtimeForwarded, string nativeIngress = "not-observed",
        string nativeAgent = "not-observed", string nativeDispatch = "not-observed",
        string route = "not-observed", bool? strictPolicyEnabled = null)
        : this(traceId, runtimeReceived, runtimeForwarded, nativeIngress, nativeAgent,
            nativeDispatch, route, strictPolicyEnabled, null)
    {
    }

    public LilithMiniGameDiagnosticReceipt(Guid traceId, string runtimeReceived,
        string runtimeForwarded, string nativeIngress, string nativeAgent, string nativeDispatch,
        string route, bool? strictPolicyEnabled, LilithMiniGameWireDiagnostic? wire)
    {
        if (traceId == Guid.Empty || runtimeReceived is not ("present" or "absent")
            || runtimeForwarded is not ("not-reached" or "present" or "absent" or "stale")
            || !IsNativePresence(nativeIngress) || !IsNativePresence(nativeAgent)
            || nativeDispatch is not ("not-observed" or "present" or "absent" or "omitted-moa")
            || route is not ("not-observed" or "chat" or "responses" or "anthropic" or "bedrock" or "other"))
            throw new ArgumentException("Mini-game diagnostic receipt rejected.");
        TraceId = traceId; RuntimeReceived = runtimeReceived; RuntimeForwarded = runtimeForwarded;
        NativeIngress = nativeIngress; NativeAgent = nativeAgent; NativeDispatch = nativeDispatch;
        Route = route; StrictPolicyEnabled = strictPolicyEnabled;
        Wire = wire;
    }

    private static bool IsNativePresence(string value) => value is "not-observed" or "fresh" or "stale" or "absent";

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("traceId", TraceId.ToString("N"));
        writer.WriteString("runtimeReceived", RuntimeReceived);
        writer.WriteString("runtimeForwarded", RuntimeForwarded);
        writer.WriteString("nativeIngress", NativeIngress);
        writer.WriteString("nativeAgent", NativeAgent);
        writer.WriteString("nativeDispatch", NativeDispatch);
        writer.WriteString("route", Route);
        if (StrictPolicyEnabled is bool enabled) writer.WriteBoolean("strictPolicyEnabled", enabled);
        else writer.WriteNull("strictPolicyEnabled");
        if (Wire is not null)
        {
            writer.WritePropertyName("wire");
            Wire.Write(writer);
        }
        writer.WriteEndObject();
    }

    public static LilithMiniGameDiagnosticReceipt Parse(JsonElement value)
    {
        var hasWire = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("wire", out _);
        if (hasWire)
            RequireExact(value, "traceId", "runtimeReceived", "runtimeForwarded", "nativeIngress",
                "nativeAgent", "nativeDispatch", "route", "strictPolicyEnabled", "wire");
        else
            RequireExact(value, "traceId", "runtimeReceived", "runtimeForwarded", "nativeIngress",
                "nativeAgent", "nativeDispatch", "route", "strictPolicyEnabled");
        var strict = value.GetProperty("strictPolicyEnabled");
        if (strict.ValueKind is not (JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Mini-game diagnostic policy flag rejected.");
        return new LilithMiniGameDiagnosticReceipt(ReadTraceId(value), ReadString(value, "runtimeReceived"),
            ReadString(value, "runtimeForwarded"), ReadString(value, "nativeIngress"), ReadString(value, "nativeAgent"),
            ReadString(value, "nativeDispatch"), ReadString(value, "route"),
            strict.ValueKind == JsonValueKind.Null ? null : strict.GetBoolean(),
            hasWire ? LilithMiniGameWireDiagnostic.Parse(value.GetProperty("wire")) : null);
    }

    internal static void RequireExact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Mini-game diagnostic object rejected.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name))
                throw new InvalidDataException("Duplicate mini-game diagnostic field rejected.");
        if (!seen.SetEquals(names))
            throw new InvalidDataException("Mini-game diagnostic fields rejected.");
    }

    internal static Guid ReadTraceId(JsonElement value)
    {
        var text = ReadString(value, "traceId");
        if (text.Length != 32 || !Guid.TryParseExact(text, "N", out var id)
            || id == Guid.Empty || id.ToString("N") != text)
            throw new InvalidDataException("Mini-game diagnostic identity rejected.");
        return id;
    }

    private static string ReadString(JsonElement value, string name)
        => value.GetProperty(name).ValueKind == JsonValueKind.String
            ? value.GetProperty(name).GetString()!
            : throw new InvalidDataException("Mini-game diagnostic string rejected.");
}

// Observation of the bounded first 32 SDK callbacks and serialized HTTP request
// bodies, not delivery or model attention. Counters are independent (for example,
// HTTP redirects/retries); last/any flags describe only the examined prefix.
// Mode describes observed SDK stream flags, not the body or remote transport.
// Native157's zero-count nonstream default remains readable as legacy evidence.
public sealed class LilithMiniGameWireDiagnostic
{
    public const int MaximumRequests = 32;
    public string Status { get; }
    public string Route { get; }
    public string Mode { get; }
    public int SdkRequests { get; }
    public int WireRequests { get; }
    public string FirstSdk { get; }
    public string LastSdk { get; }
    public string FirstWire { get; }
    public string LastWire { get; }
    public bool AnyMissing { get; }
    public bool AnyUnknown { get; }
    public bool Saturated { get; }
    public string UnsupportedReason { get; }

    public LilithMiniGameWireDiagnostic(string status, string route, string mode,
        int sdkRequests, int wireRequests, string firstSdk, string lastSdk,
        string firstWire, string lastWire, bool anyMissing, bool anyUnknown,
        bool saturated, string unsupportedReason)
    {
        if (status is not ("observed" or "not-observed" or "unsupported")
            || route is not ("chat" or "other")
            || mode is not ("nonstream" or "stream" or "mixed" or "unknown" or "not-observed")
            || (mode == "not-observed" && sdkRequests != 0)
            || (mode is "stream" or "unknown" && sdkRequests == 0)
            || (mode == "mixed" && sdkRequests < 2)
            || !ValidObservations(sdkRequests, firstSdk, lastSdk)
            || !ValidObservations(wireRequests, firstWire, lastWire)
            || unsupportedReason is not ("none" or "route" or "ownership" or "hook" or "body")
            || (status == "observed" && sdkRequests == 0 && wireRequests == 0)
            || (status == "not-observed" && (sdkRequests != 0 || wireRequests != 0))
            || ((anyMissing || anyUnknown) && sdkRequests == 0 && wireRequests == 0)
            || (!anyMissing && (firstSdk == "absent" || lastSdk == "absent" || firstWire == "absent" || lastWire == "absent"))
            || (!anyUnknown && (firstSdk == "unknown" || lastSdk == "unknown" || firstWire == "unknown" || lastWire == "unknown"))
            || (saturated && sdkRequests != MaximumRequests && wireRequests != MaximumRequests))
            throw new ArgumentException("Mini-game wire diagnostic rejected.");
        Status = status; Route = route; Mode = mode;
        SdkRequests = sdkRequests; WireRequests = wireRequests;
        FirstSdk = firstSdk; LastSdk = lastSdk; FirstWire = firstWire; LastWire = lastWire;
        AnyMissing = anyMissing; AnyUnknown = anyUnknown; Saturated = saturated;
        UnsupportedReason = unsupportedReason;
    }

    private static bool ValidObservations(int count, string first, string last)
        => count >= 0 && count <= MaximumRequests
            && (count == 0 ? first == "not-observed" && last == "not-observed"
                : IsPresence(first) && IsPresence(last));

    private static bool IsPresence(string value) => value is "present" or "absent" or "unknown";

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("status", Status);
        writer.WriteString("route", Route);
        writer.WriteString("mode", Mode);
        writer.WriteNumber("sdkRequests", SdkRequests);
        writer.WriteNumber("wireRequests", WireRequests);
        writer.WriteString("firstSdk", FirstSdk);
        writer.WriteString("lastSdk", LastSdk);
        writer.WriteString("firstWire", FirstWire);
        writer.WriteString("lastWire", LastWire);
        writer.WriteBoolean("anyMissing", AnyMissing);
        writer.WriteBoolean("anyUnknown", AnyUnknown);
        writer.WriteBoolean("saturated", Saturated);
        writer.WriteString("unsupportedReason", UnsupportedReason);
        writer.WriteEndObject();
    }

    public static LilithMiniGameWireDiagnostic Parse(JsonElement value)
    {
        LilithMiniGameDiagnosticReceipt.RequireExact(value, "status", "route", "mode",
            "sdkRequests", "wireRequests", "firstSdk", "lastSdk", "firstWire", "lastWire",
            "anyMissing", "anyUnknown", "saturated", "unsupportedReason");
        return new LilithMiniGameWireDiagnostic(ReadString(value, "status"), ReadString(value, "route"),
            ReadString(value, "mode"), ReadCount(value, "sdkRequests"), ReadCount(value, "wireRequests"),
            ReadString(value, "firstSdk"), ReadString(value, "lastSdk"), ReadString(value, "firstWire"),
            ReadString(value, "lastWire"), ReadBoolean(value, "anyMissing"), ReadBoolean(value, "anyUnknown"),
            ReadBoolean(value, "saturated"), ReadString(value, "unsupportedReason"));
    }

    private static string ReadString(JsonElement value, string name)
        => value.GetProperty(name).ValueKind == JsonValueKind.String
            ? value.GetProperty(name).GetString()!
            : throw new InvalidDataException("Mini-game wire string rejected.");

    private static int ReadCount(JsonElement value, string name)
        => value.GetProperty(name).ValueKind == JsonValueKind.Number && value.GetProperty(name).TryGetInt32(out var count)
            ? count : throw new InvalidDataException("Mini-game wire count rejected.");

    private static bool ReadBoolean(JsonElement value, string name)
        => value.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetProperty(name).GetBoolean()
            : throw new InvalidDataException("Mini-game wire flag rejected.");
}
