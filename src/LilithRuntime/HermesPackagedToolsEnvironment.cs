using System.Diagnostics;

namespace LilithRuntime;

/// <summary>Discovers recipient-local optional tools without changing native tool authority.</summary>
internal static class HermesPackagedToolsEnvironment
{
    internal const string Version = "v1-win-x64-20261008";

    internal static void Configure(ProcessStartInfo startInfo, LilithRuntimeOptions options)
    {
        startInfo.Environment.Remove("HERMES_LILITH_TOOL_PAYLOAD");
        var component = new DirectoryInfo(Path.GetFullPath(options.HermesComponentRootPath));
        if (!string.Equals(component.Parent?.Name, "hermes", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(component.Parent?.Parent?.Name, "Components", StringComparison.OrdinalIgnoreCase))
            return;
        var runtime = component.Parent!.Parent!.Parent;
        if (runtime is null) return;
        var root = Path.Combine(runtime.FullName, "Tools", Version);
        if (!File.Exists(Path.Combine(root, "tools-manifest.json"))) return;

        var paths = new List<string>();
        void Add(string directory, params string[] required)
        {
            var path = Path.Combine(root, directory);
            if (required.All(name => File.Exists(Path.Combine(path, name)))) paths.Add(path);
        }
        Add("bin", "python.cmd");
        Add("node", "node.exe", "npm.cmd", "npx.cmd");
        Add("browser", "agent-browser.exe");
        Add(Path.Combine("git", "cmd"), "git.exe");
        Add("ripgrep", "rg.exe");
        Add("ffmpeg", "ffmpeg.exe", "ffprobe.exe");
        // Expose uv through bin/uv, but not uvx globally: Hermes treats an
        // available uvx as an implicit Browser Use backend even without a
        // downloaded Browser Use installation. Explicit native setup may use
        // the shipped uv/uvx.exe without changing the player's browser choice.
        var existingKey = startInfo.Environment.Keys.FirstOrDefault(key => key.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        var existing = existingKey is null ? "" : startInfo.Environment[existingKey] ?? "";
        foreach (var key in startInfo.Environment.Keys.Where(key => key.Equals("PATH", StringComparison.OrdinalIgnoreCase)).ToArray())
            startInfo.Environment.Remove(key);
        startInfo.Environment["PATH"] = string.Join(Path.PathSeparator,
            paths.Concat(existing.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.OrdinalIgnoreCase));

        var chrome = Path.Combine(root, "chromium", "chrome-win64", "chrome.exe");
        if (File.Exists(chrome)) startInfo.Environment["AGENT_BROWSER_EXECUTABLE_PATH"] = chrome;
        var bash = Path.Combine(root, "git", "bin", "bash.exe");
        if (File.Exists(bash)) startInfo.Environment["HERMES_GIT_BASH_PATH"] = bash;
        var libraries = Path.Combine(root, "python-libs");
        if (Directory.Exists(libraries))
        {
            startInfo.Environment.TryGetValue("PYTHONPATH", out var pythonPath);
            startInfo.Environment["PYTHONPATH"] = string.Join(Path.PathSeparator,
                (pythonPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Append(libraries).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        startInfo.Environment["HERMES_LILITH_TOOL_PAYLOAD"] = root;
    }
}
