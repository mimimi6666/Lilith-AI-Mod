namespace LilithRuntime;

internal enum LilithDailyNoteDiagnosticReason
{
    NotObserved, Disabled, Unsafe, ClockUntrusted, ServiceUnavailable,
    Preparing, NativeUnavailable, UnsupportedRoute, ApprovalUiUnavailable,
    NativeBusy, NativeCapacity, PreparationRevoked, PreparationTimeout,
    PreparationError, InvalidProof, ExpiredProof, RouteMismatch, UiLeaseExpired,
    GoalIneligible, EvaluationReserved, ReconciliationRequired, StoreFault
}

// Fixed vocabulary only: no exception messages, model strings, history, tool
// output or profile paths. This is observation, not an authorization receipt.
internal sealed record LilithDailyNoteDiagnostic(Guid GenerationId,
    LilithDailyNoteDiagnosticReason Reason, DateTimeOffset ObservedAtUtc,
    DateTimeOffset? RetryNotBeforeUtc = null)
{
    internal static LilithDailyNoteDiagnosticReason NativeReason(string code) => code switch
    {
        "daily.unsupported_route" => LilithDailyNoteDiagnosticReason.UnsupportedRoute,
        "daily.approval_ui_unavailable" => LilithDailyNoteDiagnosticReason.ApprovalUiUnavailable,
        "daily.preparation_busy" => LilithDailyNoteDiagnosticReason.NativeBusy,
        "daily.preparation_duplicate_or_capacity" => LilithDailyNoteDiagnosticReason.NativeCapacity,
        _ => LilithDailyNoteDiagnosticReason.NativeUnavailable
    };
}
