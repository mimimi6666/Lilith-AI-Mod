using LilithRuntimeContracts;
using System.IO;

namespace LilithControlCenter;

// UI-only reducer. It can present offered choices, never infer native approval.
internal sealed class D059DailyNoteApprovalState
{
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly HashSet<Guid> _seen = new();
    private long _sequence;
    private LilithDailyNoteUiPresence? _latestPresence;
    private bool _attached;
    private bool _responseInFlight;
    private Guid _generation;
    private Guid? _lease;
    private DateTimeOffset? _leaseExpires;
    internal Guid UiEpoch { get; } = Guid.NewGuid();
    internal LilithDailyNoteApprovalRequest? Pending { get; private set; }
    internal string Status { get; private set; } = string.Empty;
    internal D059DailyNoteApprovalState(Func<DateTimeOffset>? utcNow = null)
        => _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    internal LilithDailyNoteUiPresence CreatePresence(Guid generation, bool attached)
    {
        if (generation == Guid.Empty) throw new ArgumentException("Ready UI generation required.");
        if (_generation != generation) { ClearAuthority(); _generation = generation; }
        _attached = attached;
        if (!attached) ClearAuthority();
        return _latestPresence = new(UiEpoch, checked(++_sequence), attached);
    }

    internal void ApplyPresence(Guid generation, LilithDailyNoteUiPresence request, LilithDailyNoteUiPresenceReceipt receipt)
    {
        LilithDailyNoteApprovalProtocol.Validate(receipt);
        if (generation != _generation || request != _latestPresence
            || receipt.UiEpoch != UiEpoch || receipt.Sequence != request.Sequence)
            throw new InvalidDataException("Daily UI presence receipt changed identity.");
        var now = _utcNow();
        if (!request.Attached || !receipt.Accepted)
        {
            if (receipt.UiLeaseId.HasValue) throw new InvalidDataException("Detached UI acquired a lease.");
            ClearAuthority(); return;
        }
        if (!_attached || receipt.UiLeaseId is null || receipt.ExpiresAtUtc is not { } expires
            || expires <= now || expires - now > LilithDailyNoteApprovalProtocol.UiLeaseLifetime)
            throw new InvalidDataException("Daily UI lease is not fresh and bounded.");
        if (_lease.HasValue && _lease != receipt.UiLeaseId) ClearAuthority();
        _attached = true; _lease = receipt.UiLeaseId; _leaseExpires = expires;
    }

    internal bool Receive(Guid generation, LilithDailyNoteApprovalRequest request)
    {
        Expire();
        if (!HasLease || generation != _generation || request.Scope.RuntimeGeneration != _generation
            || request.UiLeaseId != _lease || request.ExpiresAtUtc <= _utcNow()
            || request.ExpiresAtUtc - _utcNow() > TimeSpan.FromSeconds(60)
            || _seen.Count >= 4096 || !_seen.Add(request.ApprovalId) || Pending is not null)
            return false;
        Pending = request; _responseInFlight = false;
        Status = "莉莉絲想留一張圖片便條，正在等待你的選擇。";
        return true;
    }

    internal bool CanRespond(string choice)
    {
        Expire();
        return HasLease && Pending is { } pending && !_responseInFlight
            && pending.Choices.Contains(choice, StringComparer.Ordinal);
    }

    internal LilithDailyNoteApprovalResponse BeginResponse(string choice)
    {
        if (!CanRespond(choice)) throw new InvalidOperationException("No current offered daily approval choice.");
        _responseInFlight = true;
        Status = "正在送出你的選擇；不會自動重送。";
        var pending = Pending!;
        return new(pending.Scope, pending.ApprovalId, pending.UiLeaseId, choice);
    }

    internal void CompleteResponse(LilithDailyNoteApprovalResponse response, LilithDailyNoteApprovalReceipt receipt)
    {
        LilithDailyNoteApprovalProtocol.Validate(receipt);
        if (receipt.Scope != response.Scope || receipt.ApprovalId != response.ApprovalId
            || receipt.UiLeaseId != response.UiLeaseId || receipt.Choice != response.Choice)
            throw new InvalidDataException("Daily approval response receipt changed identity.");
        if (Pending is not { } pending || pending.Scope != response.Scope || pending.ApprovalId != response.ApprovalId)
            return; // A detached/expired prompt is never revived by a late receipt.
        Pending = null; _responseInFlight = false;
        Status = receipt.Accepted
            ? (response.Choice == "deny" ? "已拒絕這次便條請求。" : "已送出你的選擇；是否寫成圖片仍以投遞結果為準。")
            : "這次便條核准已失效或未被接受，沒有自動重送。";
    }

    internal void Close(Guid generation, LilithDailyNoteApprovalClosed value)
    {
        LilithDailyNoteApprovalProtocol.Validate(value);
        if (generation != _generation || value.Scope.RuntimeGeneration != generation) return;
        // A close can race an already queued request. Remember the identity even
        // when there is not yet a visible card; no later delivery may revive it.
        if (_seen.Count >= 4096 && !_seen.Contains(value.ApprovalId)) { FailClosed(); return; }
        _seen.Add(value.ApprovalId);
        if (Pending is { } pending && value.Scope == pending.Scope
            && value.ApprovalId == pending.ApprovalId && value.UiLeaseId == pending.UiLeaseId)
        { Pending = null; _responseInFlight = false; Status = "這次便條請求已結束，不能再回覆。"; }
    }

    internal void FailClosed()
    { ClearAuthority(); _attached = false; Status = "便條核准介面目前不可用；不會自動允許或重送。"; }
    internal void Expire()
    {
        if (!HasLease || Pending is { } pending && _utcNow() >= pending.ExpiresAtUtc)
        { Pending = null; _responseInFlight = false; }
    }
    private bool HasLease => _attached && _lease.HasValue && _leaseExpires is { } at && _utcNow() < at;
    private void ClearAuthority()
    { _lease = null; _leaseExpires = null; Pending = null; _responseInFlight = false; }
}
