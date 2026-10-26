using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class ForegroundProactiveApplicationDescriptor
{
    internal string ApplicationId { get; }
    internal string DisplayName { get; }
    internal string Category { get; }

    internal ForegroundProactiveApplicationDescriptor(
        string applicationId,
        string displayName,
        string category)
    {
        ApplicationId = applicationId;
        DisplayName = displayName;
        Category = category;
    }
}

internal static class ForegroundProactiveApplicationPolicy
{
    private static readonly HashSet<string> ExcludedProcesses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "lilith",
        "lilithcontrolcenter",
        "lilithruntime",
        "lilithvoicehost",
        "lilithproviderruntimeauthority",
        "explorer",
        "dwm",
        "sihost",
        "taskhostw",
        "applicationframehost",
        "searchhost",
        "searchapp",
        "searchui",
        "startmenuexperiencehost",
        "shellexperiencehost",
        "textinputhost",
        "runtimebroker",
        "systemsettings",
        "taskmgr",
        "control",
        "pickerhost",
        "openwith",
        "lockapp",
        "logonui",
        "credentialui",
        "credentialuibroker",
        "consent",
        "smartscreen",
        "sechealthui",
        "securityhealthhost",
        "securityhealthservice",
        "securityhealthsystray",
        "windowssecurity",
        "speechruntime",
        "voiceaccess",
        "windowsvoiceassistant",
        "keepass",
        "keepassxc",
        "bitwarden",
        "1password",
        "lastpass",
        "authy"
    };

    private static readonly IReadOnlyDictionary<string,
        ForegroundProactiveApplicationDescriptor> KnownApplications =
        new Dictionary<string, ForegroundProactiveApplicationDescriptor>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = Known("visual-studio-code", "Visual Studio Code", "coding"),
            ["cursor"] = Known("cursor", "Cursor", "coding"),
            ["devenv"] = Known("visual-studio", "Visual Studio", "coding"),
            ["rider64"] = Known("jetbrains-rider", "JetBrains Rider", "coding"),
            ["idea64"] = Known("intellij-idea", "IntelliJ IDEA", "coding"),
            ["pycharm64"] = Known("pycharm", "PyCharm", "coding"),
            ["webstorm64"] = Known("webstorm", "WebStorm", "coding"),
            ["sublime_text"] = Known("sublime-text", "Sublime Text", "coding"),
            ["notepad++"] = Known("notepad-plus-plus", "Notepad++", "coding"),
            ["windowsterminal"] = Known("windows-terminal", "Windows Terminal", "coding"),
            ["powershell"] = Known("powershell", "PowerShell", "coding"),
            ["pwsh"] = Known("powershell", "PowerShell", "coding"),
            ["cmd"] = Known("command-prompt", "Command Prompt", "coding"),
            ["spotify"] = Known("spotify", "Spotify", "music"),
            ["musicbee"] = Known("musicbee", "MusicBee", "music"),
            ["foobar2000"] = Known("foobar2000", "foobar2000", "music"),
            ["itunes"] = Known("itunes", "iTunes", "music"),
            ["chrome"] = Known("google-chrome", "Google Chrome", "browser"),
            ["msedge"] = Known("microsoft-edge", "Microsoft Edge", "browser"),
            ["firefox"] = Known("firefox", "Firefox", "browser"),
            ["brave"] = Known("brave", "Brave", "browser"),
            ["opera"] = Known("opera", "Opera", "browser"),
            ["vivaldi"] = Known("vivaldi", "Vivaldi", "browser"),
            ["vlc"] = Known("vlc", "VLC", "video"),
            ["mpv"] = Known("mpv", "mpv", "video"),
            ["potplayermini64"] = Known("potplayer", "PotPlayer", "video"),
            ["discord"] = Known("discord", "Discord", "communication"),
            ["slack"] = Known("slack", "Slack", "communication"),
            ["teams"] = Known("microsoft-teams", "Microsoft Teams", "communication"),
            ["ms-teams"] = Known("microsoft-teams", "Microsoft Teams", "communication"),
            ["telegram"] = Known("telegram", "Telegram", "communication"),
            ["line"] = Known("line", "LINE", "communication"),
            ["winword"] = Known("microsoft-word", "Microsoft Word", "document"),
            ["excel"] = Known("microsoft-excel", "Microsoft Excel", "document"),
            ["powerpnt"] = Known("microsoft-powerpoint", "Microsoft PowerPoint", "document"),
            ["acrord32"] = Known("adobe-acrobat", "Adobe Acrobat", "document"),
            ["notepad"] = Known("notepad", "Notepad", "document"),
            ["photoshop"] = Known("adobe-photoshop", "Adobe Photoshop", "creative"),
            ["illustrator"] = Known("adobe-illustrator", "Adobe Illustrator", "creative"),
            ["blender"] = Known("blender", "Blender", "creative"),
            ["unity"] = Known("unity-editor", "Unity", "creative"),
            ["chatgpt"] = Known("codex", "Codex", "coding")
        };

    internal static bool TryDescribe(
        string? processName,
        out ForegroundProactiveApplicationDescriptor? descriptor)
    {
        descriptor = null;
        var normalized = NormalizeProcessName(processName);
        if (normalized.Length == 0
            || ExcludedProcesses.Contains(normalized))
        {
            return false;
        }

        if (KnownApplications.TryGetValue(normalized, out var known))
        {
            descriptor = known;
            return true;
        }

        // Unknown executable names can contain project, customer, or private
        // product names. Preserve only the fact that another application is
        // active; do not forward the raw process token to Hermes.
        descriptor = Known("other", "Current application", "general");
        return true;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return string.Empty;
        var value = processName.Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        if (value.Length is 0 or > 128)
            return string.Empty;
        foreach (var character in value)
        {
            if (!((character >= 'a' && character <= 'z')
                  || (character >= 'A' && character <= 'Z')
                  || (character >= '0' && character <= '9')
                  || character is '.' or '_' or '-' or '+'))
            {
                return string.Empty;
            }
        }
        return value.ToLowerInvariant();
    }

    private static ForegroundProactiveApplicationDescriptor Known(
        string applicationId,
        string displayName,
        string category)
        => new(applicationId, displayName, category);
}
