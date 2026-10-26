using System.Globalization;
using System.Runtime.InteropServices;
using LilithRuntimeContracts;

namespace LilithControlCenter;

// A closed presentation boundary, not a second startup/repair authority.
// Neither arbitrary exception text nor process output is accepted here.
internal sealed class RuntimeStartupDiagnosticPresentation
{
    private RuntimeStartupDiagnosticPresentation(string code, string stage, int? exitCode,
        DateTimeOffset observedAt, Version? appVersion)
    {
        Code = code;
        Stage = stage;
        ExitCode = exitCode;
        ObservedAtUtc = observedAt.ToUniversalTime();
        AppVersion = appVersion?.ToString() ?? "unknown";
    }

    internal string Code { get; }
    internal string Stage { get; }
    internal int? ExitCode { get; }
    internal DateTimeOffset ObservedAtUtc { get; }
    internal string AppVersion { get; }
    internal string ReasonKey => "startup.reason." + Code["runtime.".Length..];
    internal string StageKey => "startup.stage." + Stage;
    internal string ActionKey => Code switch
    {
        "runtime.hermes_component_layout_mismatch" or "runtime.hermes_component_hash_mismatch"
            or "runtime.hermes_component_missing_or_unreadable" or "runtime.cua_component_verification_failed"
            or "runtime.voice_component_verification_failed" or "runtime.payload_unavailable"
            or "runtime.peer_trust_invalid" => "startup.action.installation",
        "runtime.config_verification_failed" or "runtime.profile_verification_failed"
            => "startup.action.configuration",
        "runtime.player_save_busy" => "startup.action.saveBusy",
        "runtime.player_save_restart_required" => "startup.action.saveRestart",
        _ => "startup.action.support"
    };

    internal static RuntimeStartupDiagnosticPresentation Create(string? untrustedCode,
        int? exitCode, DateTimeOffset observedAt, Version? appVersion)
    {
        // Numeric process status is the authoritative source when available.
        var code = exitCode is { } exit
            ? LilithRuntimeStartupDiagnostics.CodeForExit(exit)
            : NormalizeCode(untrustedCode);
        var stage = code switch
        {
            "runtime.payload_unavailable" or "runtime.peer_trust_invalid"
                or "runtime.launch_failed" or "runtime.launch_returned_no_process"
                or "runtime.launch_requested" => "bootstrap",
            "runtime.player_save_busy" or "runtime.player_save_restart_required" => "player_save",
            "runtime.startup_observation_elapsed" or "runtime.startup_observation_unavailable" => "observation",
            _ => LilithRuntimeStartupDiagnostics.StageForCode(code)
        };
        return new(code, stage, exitCode, observedAt, appVersion);
    }

    private static string NormalizeCode(string? value)
        => value is not null && (LilithRuntimeStartupDiagnostics.IsKnownCode(value)
            || value is "runtime.payload_unavailable" or "runtime.peer_trust_invalid"
                or "runtime.launch_failed" or "runtime.launch_returned_no_process"
                or "runtime.launch_requested" or "runtime.player_save_busy"
                or "runtime.player_save_restart_required" or "runtime.startup_observation_elapsed"
                or "runtime.startup_observation_unavailable")
            ? value : "runtime.startup_failed";

    internal string ToSafeLogLine()
        => "Runtime startup observation " + Code + " stage=" + Stage
            + " exit_code=" + NumericExitCode + " observed_utc=" + Timestamp
            + " control_center_version=" + AppVersion;

    internal string ToCopyText()
        => "Lilith startup diagnostic v1\ncode=" + Code + "\nstage=" + Stage
            + "\nexit_code=" + NumericExitCode + "\nobserved_utc=" + Timestamp
            + "\ncontrol_center_version=" + AppVersion;

    internal bool TryCopy(Action<string> copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        try { copy(ToCopyText()); return true; }
        catch (ExternalException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (ThreadStateException) { return false; }
    }

    private string Timestamp => ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture);
    private string NumericExitCode => ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
}
