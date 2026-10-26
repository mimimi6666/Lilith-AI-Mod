using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    // Called only by the admitted Control Center connection's daily channel.
    // Generic RouteAsync still rejects these methods and never observes daily
    // ownership as an active player conversation.
    internal async Task<LilithRuntimeResponse> RouteDailyApprovalAsync(Guid connection,
        LilithDailyNoteApprovalBroker broker, LilithDailyNoteApprovalResponse response, CancellationToken cancellationToken)
    {
        var reservation = broker.TryReserve(connection, response);
        if (reservation is null) return DailyApprovalReceipt(response, false, "daily.approval_unavailable");
        try
        {
            using var authority = await _modelAuthority.AcquireAsync(_generationId, cancellationToken).ConfigureAwait(false);
            if (!reservation.IsCurrent || authority.Snapshot.GenerationId != _generationId
                || authority.Snapshot.State != LilithRuntimeModelAuthorityState.Ready)
                return DailyApprovalReceipt(response, false, "daily.approval_unavailable");
            var result = await RouteApprovalResponseAsync(reservation.NativeResponse, cancellationToken,
                authority, reservation).ConfigureAwait(false);
            return DailyApprovalReceipt(response, result.Succeeded, result.Succeeded ? "ok" : "daily.approval_rejected");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The shared transaction core already quarantines uncertain
            // permanent writes. This UI response must never imply no effects.
            return DailyApprovalReceipt(response, false, "daily.approval_unknown");
        }
        finally { broker.Complete(reservation); }
    }

    private static LilithRuntimeResponse DailyApprovalReceipt(LilithDailyNoteApprovalResponse request, bool accepted, string code)
        => new(LilithDailyNoteApprovalProtocol.RespondMethod, true,
            LilithDailyNoteApprovalProtocol.EncodeReceipt(new(request.Scope, request.ApprovalId,
                request.UiLeaseId, request.Choice, accepted, accepted ? 1 : 0, code)));

    private bool TryReserveDailyNativeApproval(LilithDailyNoteApprovalReservation daily, out PendingNativeApproval approval)
    {
        approval = null!;
        if (!daily.IsCurrent) return false;
        var request = daily.Request;
        var presentation = JsonSerializer.SerializeToElement(new { pattern_key = request.PatternKey,
            command = request.Command, description = request.Description, choices = request.Choices,
            allow_session = request.AllowSession, allow_permanent = request.AllowPermanent, smart_denied = request.SmartDenied });
        if (!TryReadNativeApproval(presentation, out approval)) return false;
        lock (_sessionLock)
        {
            if (daily.Choice == "always")
            {
                if (_nativeConfigMutationInFlightSessionId is not null) return false;
                _nativeConfigMutationInFlightSessionId = request.Scope.AttemptId.ToString("N");
            }
            approval.InFlight = true;
            return true;
        }
    }

    private void ReleaseDailyNativeApproval(string ownerKey)
    {
        lock (_sessionLock)
        {
            if (_nativeConfigMutationInFlightSessionId == ownerKey)
                _nativeConfigMutationInFlightSessionId = null;
        }
    }
}
