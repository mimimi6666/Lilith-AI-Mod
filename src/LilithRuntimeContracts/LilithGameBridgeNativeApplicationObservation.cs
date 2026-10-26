using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LilithGameBridgeContracts;

// A transient, authenticated game-host observation. Neither speech text nor
// the presence of this record grants screenshot permission.
public sealed class LilithGameBridgeNativeApplicationObservation
{
    public string ApplicationId { get; }
    public string ApplicationName { get; }
    public string Category { get; }
    public long ObservedAtUnixMilliseconds { get; }
    public long WindowHandle { get; }
    public int ProcessId { get; }
    public long ProcessStartedAtUnixMilliseconds { get; }

    public LilithGameBridgeNativeApplicationObservation(string applicationId, string applicationName,
        string category, long observedAtUnixMilliseconds, long windowHandle, int processId,
        long processStartedAtUnixMilliseconds)
    {
        ApplicationId = LilithGameBridgeProtocol.ValidateApplicationToken(applicationId, nameof(applicationId), 128);
        ApplicationName = LilithGameBridgeProtocol.ValidateApplicationDisplayName(applicationName, nameof(applicationName));
        Category = LilithGameBridgeProtocol.ValidateApplicationToken(category, nameof(category), 64);
        if (applicationId == "other" || observedAtUnixMilliseconds is <= 0 or > 253402300799999
            || windowHandle <= 0 || processId <= 0 || processStartedAtUnixMilliseconds <= 0
            || processStartedAtUnixMilliseconds > observedAtUnixMilliseconds)
            throw new ArgumentException("Application observation identity is invalid.");
        ObservedAtUnixMilliseconds = observedAtUnixMilliseconds;
        WindowHandle = windowHandle;
        ProcessId = processId;
        ProcessStartedAtUnixMilliseconds = processStartedAtUnixMilliseconds;
    }

    public static bool IsApplicationTrigger(string trigger) => trigger is
        "AppAwareBrowsing" or "AppAwareDesign" or "AppAwareChat" or "AppAwareCoding"
        or "AppAwareCodingLate" or "AppAwareCodingMusic" or "AppAwareMusicOnly"
        or "AppAwareOffice" or "AppAwareCrowded" or "AppAwareVideo";

    public Dictionary<string, object> ToNativePayload() => new()
    {
        ["application_id"] = ApplicationId, ["application_name"] = ApplicationName,
        ["category"] = Category, ["observed_at_unix_ms"] = ObservedAtUnixMilliseconds,
        ["window_handle"] = WindowHandle, ["process_id"] = ProcessId,
        ["process_started_at_unix_ms"] = ProcessStartedAtUnixMilliseconds
    };

    public static LilithGameBridgeNativeApplicationObservation DecodeNative(JsonElement value)
    {
        LilithGameBridgeContinuationContext.RequireFields(value, "application_id", "application_name", "category",
            "observed_at_unix_ms", "window_handle", "process_id", "process_started_at_unix_ms");
        return new(value.GetProperty("application_id").GetString()!, value.GetProperty("application_name").GetString()!,
            value.GetProperty("category").GetString()!, value.GetProperty("observed_at_unix_ms").GetInt64(),
            value.GetProperty("window_handle").GetInt64(), value.GetProperty("process_id").GetInt32(),
            value.GetProperty("process_started_at_unix_ms").GetInt64());
    }
}
