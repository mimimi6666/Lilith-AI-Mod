using System.Globalization;
using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public enum UsageBudgetFailureKind { UnsupportedRoute, InputTooLarge, TurnExhausted, Stopped, Other,
    LocalCompatibilityCapacity, LocalCompatibilityIncomplete, LocalCompatibilityRoute }

// Native typed terminal metadata, not model prose or a permission decision.
public sealed record UsageBudgetFailure(string Code, UsageBudgetFailureKind Kind);

public sealed record UsageBudgetConstraint(string WireFormat, string Parameter, long? RetainedValue, string Reason);

internal static class UsageBudgetConstraintPresentation
{
    internal static IReadOnlyList<UsageBudgetConstraint> Decode(JsonElement payload)
        => HermesUsageBudgetTerminalMetadata.DecodeConstraints(payload)
            .Select(value => new UsageBudgetConstraint(value.WireFormat, value.Parameter,
                value.RetainedValue, value.Reason)).ToList().AsReadOnly();

    internal static string Display(IReadOnlyList<UsageBudgetConstraint> constraints)
    {
        var t = ControlCenterLocalization.Current;
        return t["budget.constraints.intro"] + "\n" + string.Join("\n", constraints.Select(c =>
        {
            var label = c.Parameter switch
            {
                "context_length" => "context",
                "threshold_tokens_cap" => "compressionTokens",
                "threshold_basis_points" => "compressionBase",
                "max_tokens" => "output",
                _ => "thinking"
            };
            var numeric = c.RetainedValue is not { } retained ? t["budget.constraints.unknown"]
                : c.Parameter == "threshold_basis_points"
                    ? (retained / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%"
                    : retained.ToString(CultureInfo.InvariantCulture);
            return t.Format("budget.constraints.item", t["budget.constraints." + label], numeric,
                t["budget.constraints." + c.Reason]);
        })) + "\n" + t["budget.constraints.scope"];
    }
}

internal static class UsageBudgetErrorPresentation
{
    internal static UsageBudgetFailure Parse(string code)
    {
        _ = HermesUsageBudgetTerminalMetadata.ParseErrorCode(code);
        var kind = code switch
        {
            "local_compatibility_context_unavailable" or "local_compatibility_route_changed"
                or "local_compatibility_auxiliary_stream_unsupported" => UsageBudgetFailureKind.LocalCompatibilityRoute,
            "local_compatibility_input_too_large" or "local_compatibility_output_cap_invalid"
                or "local_compatibility_output_cap_conflict" or "local_compatibility_summary_fixed_input_too_large"
                or "local_compatibility_summary_output_too_large" => UsageBudgetFailureKind.LocalCompatibilityCapacity,
            "local_compatibility_summary_chunk_limit" or "local_compatibility_summary_deadline"
                or "local_compatibility_summary_incomplete" or "local_compatibility_summary_empty"
                or "local_compatibility_summary_failed"
                => UsageBudgetFailureKind.LocalCompatibilityIncomplete,
            "native_budget_route_not_integrated" or "native_budget_route_unverified"
                or "native_unbounded_route_not_integrated" or "auxiliary_origin_route_unavailable"
                or "policy_capabilities_unavailable" or "wire_capability_unverified"
                or "wire_route_unverified" or "strict_output_cap_unverified"
                or "strict_input_count_unverified" or "unbounded_wire_format_unsupported"
                or "stream_accounting_not_integrated" => UsageBudgetFailureKind.UnsupportedRoute,
            "mandatory_context_exceeds_allowance" or "mandatory_input_exceeds_model_capacity"
                or "mandatory_input_exceeds_normal_capacity" or "request_input_limit"
                => UsageBudgetFailureKind.InputTooLarge,
            "turn_call_limit" or "turn_token_limit" or "actual_usage_exceeded_allowance"
                => UsageBudgetFailureKind.TurnExhausted,
            "turn_stopped" => UsageBudgetFailureKind.Stopped,
            _ => UsageBudgetFailureKind.Other
        };
        return new(code, kind);
    }

    internal static string Display(UsageBudgetFailure failure) => ControlCenterLocalization.Current[
        failure.Kind switch
        {
            UsageBudgetFailureKind.LocalCompatibilityCapacity => "local.compatibility.errorCapacity",
            UsageBudgetFailureKind.LocalCompatibilityIncomplete => "local.compatibility.errorIncomplete",
            UsageBudgetFailureKind.LocalCompatibilityRoute => "local.compatibility.errorRoute",
            UsageBudgetFailureKind.UnsupportedRoute => "budget.error.unsupported",
            UsageBudgetFailureKind.InputTooLarge => "budget.error.input",
            UsageBudgetFailureKind.TurnExhausted => "budget.error.exhausted",
            UsageBudgetFailureKind.Stopped => "budget.error.stopped",
            _ => "budget.error.other"
        }];
}
