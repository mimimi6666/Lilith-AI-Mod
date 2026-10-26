using LilithRuntimeContracts;
using System.IO;

namespace LilithControlCenter;

public interface ID059DailyNoteApprovalProjection
{
    Guid RuntimeGeneration { get; }
    bool DailyNoteApprovalChannelAvailable { get; }
    event EventHandler<LilithDailyNoteApprovalRequest>? DailyNoteApprovalRequested;
    event EventHandler<LilithDailyNoteApprovalClosed>? DailyNoteApprovalClosed;
    Task<LilithDailyNoteUiPresenceReceipt> PublishDailyNoteUiPresenceAsync(LilithDailyNoteUiPresence request, CancellationToken cancellationToken);
    Task<LilithDailyNoteApprovalReceipt> RespondDailyNoteApprovalAsync(LilithDailyNoteApprovalResponse request, CancellationToken cancellationToken);
}

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : ID059DailyNoteApprovalProjection
{
    private int _dailyApprovalFaulted;
    Guid ID059DailyNoteApprovalProjection.RuntimeGeneration => RuntimeGeneration;
    public bool DailyNoteApprovalChannelAvailable => Volatile.Read(ref _dailyApprovalFaulted) == 0 && !IsFaultedClosed;
    public event EventHandler<LilithDailyNoteApprovalRequest>? DailyNoteApprovalRequested;
    public event EventHandler<LilithDailyNoteApprovalClosed>? DailyNoteApprovalClosed;

    public async Task<LilithDailyNoteUiPresenceReceipt> PublishDailyNoteUiPresenceAsync(LilithDailyNoteUiPresence request, CancellationToken cancellationToken)
    {
        var payload = LilithDailyNoteApprovalProtocol.EncodePresence(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (!DailyNoteApprovalChannelAvailable) throw new InvalidOperationException("Daily approval channel is unavailable.");
        var generation = RuntimeGeneration;
        try
        {
            return await SendRequestAsync(new(LilithDailyNoteApprovalProtocol.PresenceMethod, payload), (response, context) =>
            {
                if (!response.Succeeded || context.GenerationId != generation) throw new InvalidDataException("Daily presence response rejected.");
                var receipt = LilithDailyNoteApprovalProtocol.ReadPresenceReceipt(response.HermesPayload);
                if (receipt.UiEpoch != request.UiEpoch || receipt.Sequence != request.Sequence
                    || !request.Attached && receipt.UiLeaseId.HasValue)
                    throw new InvalidDataException("Daily presence identity changed.");
                return receipt;
            }, _lifetime.Token, responseTimeout: TimeSpan.FromSeconds(3), isDecodeFailureIsolated: true,
                preserveLateResponseOnTimeout: true).ConfigureAwait(false);
        }
        catch { Interlocked.Exchange(ref _dailyApprovalFaulted, 1); throw; }
    }

    public async Task<LilithDailyNoteApprovalReceipt> RespondDailyNoteApprovalAsync(LilithDailyNoteApprovalResponse request, CancellationToken cancellationToken)
    {
        var payload = LilithDailyNoteApprovalProtocol.EncodeResponse(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (!DailyNoteApprovalChannelAvailable || RuntimeGeneration != request.Scope.RuntimeGeneration
            || DateTimeOffset.UtcNow >= request.Scope.ExpiresAtUtc)
            throw new InvalidOperationException("Daily approval scope is unavailable.");
        try
        {
            return await SendRequestAsync(new(LilithDailyNoteApprovalProtocol.RespondMethod, payload), (response, context) =>
            {
                if (!response.Succeeded || context.GenerationId != request.Scope.RuntimeGeneration) throw new InvalidDataException("Daily approval response rejected.");
                var receipt = LilithDailyNoteApprovalProtocol.ReadReceipt(response.HermesPayload);
                if (receipt.Scope != request.Scope || receipt.ApprovalId != request.ApprovalId
                    || receipt.UiLeaseId != request.UiLeaseId || receipt.Choice != request.Choice)
                    throw new InvalidDataException("Daily approval receipt changed identity.");
                return receipt;
            }, _lifetime.Token, responseTimeout: request.Choice == "always" ? _permanentApprovalResponseTimeout : TimeSpan.FromSeconds(10),
                isDecodeFailureIsolated: true, preserveLateResponseOnTimeout: true).ConfigureAwait(false);
        }
        catch { Interlocked.Exchange(ref _dailyApprovalFaulted, 1); throw; }
    }

    private bool TryProcessDailyApprovalEvent(LilithRuntimeHermesEvent value, D046HermesRuntimeEnvelopeContext context)
    {
        if (value.Method is not (LilithDailyNoteApprovalProtocol.ApprovalEvent or LilithDailyNoteApprovalProtocol.ClosedEvent)) return false;
        if (!DailyNoteApprovalChannelAvailable) return true;
        try
        {
            if (value.Method == LilithDailyNoteApprovalProtocol.ApprovalEvent)
            {
                var request = LilithDailyNoteApprovalProtocol.ReadRequest(value.HermesPayload);
                if (request.Scope.RuntimeGeneration != context.GenerationId) throw new InvalidDataException("Daily approval generation changed.");
                DailyNoteApprovalRequested?.Invoke(this, request);
            }
            else
            {
                var closed = LilithDailyNoteApprovalProtocol.ReadClosed(value.HermesPayload);
                if (closed.Scope.RuntimeGeneration != context.GenerationId) throw new InvalidDataException("Daily approval generation changed.");
                DailyNoteApprovalClosed?.Invoke(this, closed);
            }
        }
        catch { Interlocked.Exchange(ref _dailyApprovalFaulted, 1); }
        return true;
    }
}
