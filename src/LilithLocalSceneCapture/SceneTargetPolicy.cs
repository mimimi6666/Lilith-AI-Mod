using System;
using System.Collections.Generic;

namespace LilithLocalSceneCapture;

// A bounded identity snapshot, never titles, UI text, or pixels.
public sealed record SceneWindow(long Handle, string Key, string Process, int[] Bounds, long Monitor,
    bool Visible = true, bool Minimized = false, bool Cloaked = false, bool ToolWindow = false, bool Shell = false);

public sealed class SceneCaptureFailure : Exception
{
    public string Code { get; }
    public SceneCaptureFailure(string code) : base("Local scene capture unavailable") => Code = code;
}

public static class SceneTargetPolicy
{
    public static bool IsOwn(string process) => process.ToLowerInvariant() is
        "lilith" or "lilithcontrolcenter" or "lilithruntime" or "lilithvoicehost";

    public static bool IsSensitive(string process) => process.ToLowerInvariant() is
        "1password" or "bitwarden" or "keepass" or "keepassxc" or "lastpass" or "authy" or
        "credentialuibroker" or "credentialui" or "consent" or "logonui" or "winlogon";

    private static bool Visible(SceneWindow window) => window.Handle > 0 && window.Visible &&
        !window.Minimized && !window.Cloaked && window.Bounds.Length == 4 &&
        (long)window.Bounds[2] - window.Bounds[0] >= 96 &&
        (long)window.Bounds[3] - window.Bounds[1] >= 96;

    public static SceneWindow Choose(SceneWindow foreground, IEnumerable<SceneWindow> topToBottom, string trigger)
    {
        if (trigger is not ("player_share" or "proactive")) throw new SceneCaptureFailure("invalid_trigger");
        if (!Visible(foreground) || foreground.Monitor == 0) throw new SceneCaptureFailure("no_content_window");
        if (IsSensitive(foreground.Process)) throw new SceneCaptureFailure("sensitive_window");
        if (!IsOwn(foreground.Process))
        {
            if (foreground.Shell || foreground.ToolWindow) throw new SceneCaptureFailure("no_content_window");
            return foreground;
        }
        // Only an explicit player share may look through our own dialogue UI.
        // Proactive reads still require the real external foreground window.
        if (trigger != "player_share") throw new SceneCaptureFailure("own_window_foreground");
        int count = 0;
        foreach (var candidate in topToBottom)
        {
            if (++count > 512) throw new SceneCaptureFailure("target_selection_limit");
            if (!Visible(candidate) || candidate.Monitor != foreground.Monitor || candidate.Shell) continue;
            if (IsOwn(candidate.Process)) continue;
            // Never skip a sensitive top window to inspect something underneath.
            if (IsSensitive(candidate.Process)) throw new SceneCaptureFailure("sensitive_window");
            if (candidate.ToolWindow) throw new SceneCaptureFailure("ambiguous_content_window");
            return candidate;
        }
        throw new SceneCaptureFailure("no_content_window");
    }

    public static bool SameTarget(SceneWindow expected, SceneWindow actual) =>
        expected.Handle == actual.Handle && expected.Key == actual.Key && expected.Process == actual.Process &&
        expected.Bounds.AsSpan().SequenceEqual(actual.Bounds);
}
