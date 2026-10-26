using System.Text.Json;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;

namespace LilithRuntime;

// One explicitly leased player submit. Only closed metadata enters this object;
// it cannot hold the game snapshot, user text, provider response or exception.
internal sealed class LilithMiniGameDiagnosticCollector
{
    internal const string EventType = "game.context_diagnostic";
    internal const int MaximumPendingEvents = 6;
    private readonly object _gate = new();
    private readonly string _received;
    private readonly List<StageObservation> _pending = new();
    private readonly Dictionary<string, StageObservation> _stages = new(StringComparer.Ordinal);
    private string? _sessionId;
    private string? _turnId;
    private string _forwarded = "not-reached";

    internal LilithMiniGameDiagnosticRequest Request { get; }

    internal LilithMiniGameDiagnosticCollector(LilithMiniGameDiagnosticRequest request, bool contextPresent)
    {
        Request = request;
        _received = contextPresent ? "present" : "absent";
    }

    internal bool BeginDispatch(string sessionId, string forwarded, long now)
    {
        lock (_gate)
        {
            if (!Request.IsActive(now) || _sessionId is not null || string.IsNullOrWhiteSpace(sessionId)
                || forwarded is not ("present" or "absent" or "stale")) return false;
            _sessionId = sessionId;
            _forwarded = forwarded;
            return true;
        }
    }

    internal void BindTurn(string nativeTurnId, long now)
    {
        lock (_gate)
        {
            if (!Request.IsActive(now) || _sessionId is null || _turnId is not null
                || !CanonicalIdentity(nativeTurnId, out _)) return;
            _turnId = nativeTurnId;
            foreach (var observation in _pending)
                if (observation.TurnId == nativeTurnId)
                    _stages.TryAdd(observation.Stage, observation);
            _pending.Clear();
        }
    }

    internal void Observe(HermesGatewayEvent gatewayEvent, long now)
    {
        lock (_gate)
        {
            if (!Request.IsActive(now) || _sessionId is null
                || gatewayEvent.Type != EventType || gatewayEvent.SessionId != _sessionId) return;
            StageObservation observation;
            try
            {
                var nativeTurnId = HermesTurnCorrelationContract.ValidateGatewayEvent(EventType, gatewayEvent.Parameters);
                var payload = gatewayEvent.Payload;
                if (payload.ValueKind != JsonValueKind.Object) return;
                var fields = payload.EnumerateObject().ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal)
                    { "traceId", "turnId", "stage", "status", "route", "strictPolicyEnabled" };
                var stage = ReadString(payload, "stage");
                if (stage == "wire")
                    names.UnionWith(new[] { "mode", "sdkRequests", "wireRequests", "firstSdk", "lastSdk",
                        "firstWire", "lastWire", "anyMissing", "anyUnknown", "saturated", "unsupportedReason" });
                if (fields.Length != names.Count || !names.SetEquals(fields.Select(field => field.Name))) return;
                var trace = ReadString(payload, "traceId");
                var turn = ReadString(payload, "turnId");
                var status = ReadString(payload, "status");
                var route = ReadString(payload, "route");
                var strict = payload.GetProperty("strictPolicyEnabled");
                if (!CanonicalIdentity(trace, out var traceId) || traceId != Request.TraceId
                    || !CanonicalIdentity(turn, out _) || turn != nativeTurnId
                    || stage is not ("ingress" or "agent" or "dispatch" or "wire")) return;
                if (stage == "wire")
                {
                    if (strict.ValueKind != JsonValueKind.Null) return;
                    var wire = new LilithMiniGameWireDiagnostic(status, route, ReadString(payload, "mode"),
                        payload.GetProperty("sdkRequests").GetInt32(), payload.GetProperty("wireRequests").GetInt32(),
                        ReadString(payload, "firstSdk"), ReadString(payload, "lastSdk"),
                        ReadString(payload, "firstWire"), ReadString(payload, "lastWire"),
                        payload.GetProperty("anyMissing").GetBoolean(), payload.GetProperty("anyUnknown").GetBoolean(),
                        payload.GetProperty("saturated").GetBoolean(), ReadString(payload, "unsupportedReason"));
                    observation = new StageObservation(turn, stage, status, route, null, wire);
                }
                else
                {
                    if ((stage == "dispatch" ? status is not ("present" or "absent" or "omitted-moa")
                            : status is not ("fresh" or "stale" or "absent"))
                        || route is not ("not-observed" or "chat" or "responses" or "anthropic" or "bedrock" or "other")
                        || strict.ValueKind is not (JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False)
                        || (stage != "dispatch" && (route != "not-observed" || strict.ValueKind != JsonValueKind.Null))) return;
                    observation = new StageObservation(turn, stage, status, route,
                        strict.ValueKind == JsonValueKind.Null ? null : strict.GetBoolean());
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
                or KeyNotFoundException or ArgumentException or FormatException)
            {
                return;
            }
            if (_turnId is null)
            {
                // Events can beat prompt.submit's receipt. Retain at most six
                // content-free candidates; none is exposed before exact binding.
                if (_pending.Count < MaximumPendingEvents
                    && !_pending.Any(existing => existing.TurnId == observation.TurnId && existing.Stage == observation.Stage))
                    _pending.Add(observation);
            }
            else if (observation.TurnId == _turnId)
                _stages.TryAdd(observation.Stage, observation);
        }
    }

    internal LilithMiniGameDiagnosticReceipt? Snapshot(long now)
    {
        lock (_gate)
        {
            if (!Request.IsActive(now))
            {
                _pending.Clear();
                _stages.Clear();
                return null;
            }
            _stages.TryGetValue("dispatch", out var dispatch);
            _stages.TryGetValue("wire", out var wire);
            return new LilithMiniGameDiagnosticReceipt(Request.TraceId, _received, _forwarded,
                _stages.GetValueOrDefault("ingress")?.Status ?? "not-observed",
                _stages.GetValueOrDefault("agent")?.Status ?? "not-observed",
                dispatch?.Status ?? "not-observed", dispatch?.Route ?? "not-observed", dispatch?.StrictPolicyEnabled,
                wire?.Wire);
        }
    }

    private static bool CanonicalIdentity(string text, out Guid identity)
        => Guid.TryParseExact(text, "N", out identity) && identity != Guid.Empty && identity.ToString("N") == text;

    private static string ReadString(JsonElement value, string field)
        => value.GetProperty(field).ValueKind == JsonValueKind.String
            ? value.GetProperty(field).GetString()! : throw new InvalidDataException("Diagnostic field rejected.");

    private sealed record StageObservation(string TurnId, string Stage, string Status,
        string Route, bool? StrictPolicyEnabled, LilithMiniGameWireDiagnostic? Wire = null);
}
