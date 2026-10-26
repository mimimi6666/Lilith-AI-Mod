using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

// Connection/UI liveness and exact daily ownership only. Native approval policy
// and permanent-config transactions remain in the existing native authority.
internal sealed class LilithDailyNoteApprovalBroker
{
    private readonly object _sync = new();
    private readonly Func<Guid> _generation;
    private readonly Func<bool> _ready;
    private readonly TimeProvider _time;
    private Guid _connection;
    private Guid _connectionGeneration;
    private Guid _uiEpoch;
    private Guid? _uiLease;
    private long _sequence;
    private long _uiRenewed;
    private DateTimeOffset _uiExpires;
    private LilithDailyNoteScope? _active;
    private Guid? _activeUiLease;
    private long _activeStarted;
    private TimeSpan _activeLifetime;
    private LilithDailyNoteApprovalRequest? _pending;
    private bool _observed;
    private bool _inFlight;

    internal event Action<string, JsonElement>? UiEvent;

    internal LilithDailyNoteApprovalBroker(Func<Guid> generation, Func<bool>? ready = null, TimeProvider? time = null)
    { _generation = generation; _ready = ready ?? (() => true); _time = time ?? TimeProvider.System; }

    internal void ConnectUi(Guid connection)
    {
        if (connection == Guid.Empty) throw new ArgumentException("An admitted UI connection identity is required.");
        LilithDailyNoteApprovalClosed? closed = null;
        lock (_sync)
        {
            if (_pending is { } request) closed = CloseLocked(request, "daily.ui_reconnected");
            _connection = connection; _connectionGeneration = _generation();
            _uiEpoch = Guid.Empty; _sequence = 0; _uiLease = null;
        }
        EmitClosed(closed);
    }

    internal void DisconnectUi(Guid connection)
    {
        LilithDailyNoteApprovalClosed? closed = null;
        lock (_sync)
        {
            if (connection != _connection) return;
            _connection = Guid.Empty; _uiLease = null;
            if (_pending is { } request) closed = CloseLocked(request, "daily.ui_disconnected");
        }
        EmitClosed(closed);
    }

    internal LilithDailyNoteUiPresenceReceipt Presence(Guid connection, LilithDailyNoteUiPresence value)
    {
        LilithDailyNoteApprovalProtocol.Validate(value);
        LilithDailyNoteApprovalClosed? closed = null;
        LilithDailyNoteUiPresenceReceipt result;
        lock (_sync)
        {
            if (!ConnectionCurrent(connection) || value.Sequence <= _sequence
                || _uiEpoch != Guid.Empty && _uiEpoch != value.UiEpoch)
                return new(value.UiEpoch, value.Sequence, null, null, false);
            _uiEpoch = value.UiEpoch; _sequence = value.Sequence;
            var wasCurrent = UiCurrentLocked(_uiLease);
            if (!value.Attached)
            {
                _uiLease = null;
                if (_pending is { } request) closed = CloseLocked(request, "daily.ui_detached");
                result = new(value.UiEpoch, value.Sequence, null, null, true);
            }
            else
            {
                if (!wasCurrent)
                {
                    _uiLease = Guid.NewGuid();
                    if (_pending is { } request) closed = CloseLocked(request, "daily.ui_expired");
                }
                _uiRenewed = _time.GetTimestamp();
                _uiExpires = _time.GetUtcNow().Add(LilithDailyNoteApprovalProtocol.UiLeaseLifetime);
                result = new(value.UiEpoch, value.Sequence, _uiLease, _uiExpires, true);
            }
        }
        EmitClosed(closed);
        return result;
    }

    internal Guid? GetUiLeaseId()
    { lock (_sync) return UiCurrentLocked(_uiLease) ? _uiLease : null; }
    internal bool IsUiLeaseCurrent(Guid lease)
    { lock (_sync) return UiCurrentLocked(lease); }

    internal bool RegisterActive(LilithDailyNoteScope scope, Guid? uiLeaseId)
    {
        lock (_sync)
        {
            if (!_ready() || scope.RuntimeGeneration != _generation() || scope.ExpiresAtUtc <= _time.GetUtcNow()
                || scope.ExpiresAtUtc - _time.GetUtcNow() > TimeSpan.FromSeconds(60)
                || uiLeaseId.HasValue && !UiCurrentLocked(uiLeaseId)) return false;
            if (_active is not null) return _active == scope && _activeUiLease == uiLeaseId;
            _active = scope; _activeUiLease = uiLeaseId;
            _activeStarted = _time.GetTimestamp(); _activeLifetime = scope.ExpiresAtUtc - _time.GetUtcNow();
            _pending = null; _observed = false; _inFlight = false;
            return true;
        }
    }

    internal void RevokeActive(LilithDailyNoteScope scope)
    {
        LilithDailyNoteApprovalClosed? closed = null;
        lock (_sync)
        {
            if (_active != scope) return;
            if (_pending is { } request) closed = CloseLocked(request, "daily.task_closed");
            _active = null; _activeUiLease = null;
        }
        EmitClosed(closed);
    }

    internal bool ObserveNativeEvent(HermesGatewayEvent value)
    {
        if (value.Type is not (LilithDailyNoteApprovalProtocol.ApprovalEvent or LilithDailyNoteApprovalProtocol.ClosedEvent)) return false;
        try
        {
            if (value.Type == LilithDailyNoteApprovalProtocol.ApprovalEvent)
            {
                var request = LilithDailyNoteApprovalProtocol.ReadRequest(value.Payload);
                lock (_sync)
                {
                    if (!ActiveCurrentLocked(request.Scope) || _observed || request.UiLeaseId != _activeUiLease
                        || !UiCurrentLocked(request.UiLeaseId) || request.ExpiresAtUtc <= _time.GetUtcNow()
                        || value.SessionId != request.Scope.AttemptId.ToString("N")) return true;
                    _pending = request; _observed = true;
                }
                Emit(value.Type, LilithDailyNoteApprovalProtocol.EncodeRequest(request));
            }
            else
            {
                var closed = LilithDailyNoteApprovalProtocol.ReadClosed(value.Payload);
                lock (_sync)
                {
                    if (value.SessionId != closed.Scope.AttemptId.ToString("N")) return true;
                    if (_pending is { } request)
                    {
                        if (request.Scope != closed.Scope || request.ApprovalId != closed.ApprovalId
                            || request.UiLeaseId != closed.UiLeaseId) return true;
                    }
                    else
                    {
                        // Native cancellation may reach this observer before
                        // an already-queued request. This scope admits only one
                        // approval; an exact early close consumes that slot.
                        if (_observed || !ActiveCurrentLocked(closed.Scope)
                            || closed.UiLeaseId != _activeUiLease) return true;
                        _observed = true;
                    }
                    _pending = null; _inFlight = false;
                }
                EmitClosed(closed);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or FormatException)
        { /* Malformed native events cannot become a user approval request. */ }
        return true;
    }

    internal LilithDailyNoteApprovalReservation? TryReserve(Guid connection, LilithDailyNoteApprovalResponse response)
    {
        LilithDailyNoteApprovalProtocol.Validate(response);
        lock (_sync)
        {
            if (!ConnectionCurrent(connection) || !PendingCurrentLocked(response) || _inFlight) return null;
            _inFlight = true;
            return new(this, connection, _pending!, response.Choice);
        }
    }

    internal bool IsCurrent(LilithDailyNoteApprovalReservation reservation)
    {
        lock (_sync) return ReferenceEquals(reservation.Owner, this) && _inFlight
            && ConnectionCurrent(reservation.Connection) && ReferenceEquals(reservation.Request, _pending)
            && PendingCurrentLocked(reservation.Response);
    }

    internal void Complete(LilithDailyNoteApprovalReservation reservation)
    {
        LilithDailyNoteApprovalClosed? closed = null;
        lock (_sync)
        {
            if (ReferenceEquals(reservation.Owner, this) && ReferenceEquals(reservation.Request, _pending))
                closed = CloseLocked(reservation.Request, "daily.approval.closed");
        }
        EmitClosed(closed);
    }

    private bool PendingCurrentLocked(LilithDailyNoteApprovalResponse value)
        => _pending is { } request && request.Scope == value.Scope && request.ApprovalId == value.ApprovalId
            && request.UiLeaseId == value.UiLeaseId && request.ExpiresAtUtc > _time.GetUtcNow()
            && ActiveCurrentLocked(value.Scope) && UiCurrentLocked(value.UiLeaseId)
            && request.Choices.Contains(value.Choice, StringComparer.Ordinal);
    private bool ActiveCurrentLocked(LilithDailyNoteScope scope)
        => _ready() && _active == scope && scope.RuntimeGeneration == _generation()
            && scope.ExpiresAtUtc > _time.GetUtcNow()
            && _time.GetElapsedTime(_activeStarted) >= TimeSpan.Zero && _time.GetElapsedTime(_activeStarted) < _activeLifetime;
    private bool ConnectionCurrent(Guid connection)
        => connection != Guid.Empty && _connection == connection && _connectionGeneration != Guid.Empty
            && _connectionGeneration == _generation() && _ready();
    private bool UiCurrentLocked(Guid? lease)
        => lease.HasValue && _uiLease == lease && ConnectionCurrent(_connection) && _uiExpires > _time.GetUtcNow()
            && _time.GetElapsedTime(_uiRenewed) >= TimeSpan.Zero
            && _time.GetElapsedTime(_uiRenewed) < LilithDailyNoteApprovalProtocol.UiLeaseLifetime;
    private LilithDailyNoteApprovalClosed CloseLocked(LilithDailyNoteApprovalRequest request, string code)
    { _pending = null; _inFlight = false; return new(request.Scope, request.ApprovalId, request.UiLeaseId, code); }
    private void EmitClosed(LilithDailyNoteApprovalClosed? value)
    { if (value is not null) Emit(LilithDailyNoteApprovalProtocol.ClosedEvent, LilithDailyNoteApprovalProtocol.EncodeClosed(value)); }
    private void Emit(string type, JsonElement payload) => UiEvent?.Invoke(type, payload);
}

internal sealed record LilithDailyNoteApprovalReservation(LilithDailyNoteApprovalBroker Owner, Guid Connection,
    LilithDailyNoteApprovalRequest Request, string Choice)
{
    internal LilithDailyNoteApprovalResponse Response => new(Request.Scope, Request.ApprovalId, Request.UiLeaseId, Choice);
    internal bool IsCurrent => Owner.IsCurrent(this);
    internal JsonElement NativeResponse => LilithDailyNoteApprovalProtocol.EncodeResponse(Response);
    internal JsonElement NativePhaseTwo(string authority, string nonce, string value, bool abort)
        => abort ? JsonSerializer.SerializeToElement(new { scope = LilithDailyNoteProtocol.EncodeScope(Request.Scope),
            approval_id = Request.ApprovalId.ToString("N"), ui_lease_id = Request.UiLeaseId.ToString("N"),
            authority, commit_nonce = nonce, reason = value })
        : JsonSerializer.SerializeToElement(new { scope = LilithDailyNoteProtocol.EncodeScope(Request.Scope),
            approval_id = Request.ApprovalId.ToString("N"), ui_lease_id = Request.UiLeaseId.ToString("N"),
            authority, commit_nonce = nonce, receipt_sha256 = value });
}
