using System.Diagnostics;

namespace LilithRuntime;

/// <summary>Process-local discovery of the optional, sealed CLI payload.</summary>
internal static class HermesManagedCliEnvironment
{
    internal static bool Configure(ProcessStartInfo startInfo, LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        foreach (var key in new[] { "HERMES_LILITH_CLI_COMPONENT", "HERMES_LILITH_BOUND_HOME", "HERMES_LILITH_CLI_VOICE_SITE", "HERMES_LILITH_PYTHON_DEPENDENCIES_ROOT" })
            startInfo.Environment.Remove(key);

        var component = Path.GetFullPath(options.HermesComponentRootPath);
        var cli = Path.Combine(component, "cli");
        var files = new[] { "hermes", "hermes.cmd", "entry.py" };
        var count = files.Count(file => File.Exists(Path.Combine(cli, file)));
        // Backward-compatible with the old sealed component: no fake readiness
        // and no global fallback installation or automatic file materialization.
        if (!Directory.Exists(cli) && count == 0)
            return false;
        if (count != files.Length)
            throw new LilithRuntimeException("The packaged Hermes CLI is incomplete.");

        var pathKey = startInfo.Environment.Keys.FirstOrDefault(key =>
            key.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        var inherited = pathKey is null ? string.Empty : startInfo.Environment[pathKey] ?? string.Empty;
        var paths = inherited.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !path.Equals(cli, StringComparison.OrdinalIgnoreCase));
        foreach (var key in startInfo.Environment.Keys.Where(key => key.Equals("PATH", StringComparison.OrdinalIgnoreCase)).ToArray())
            startInfo.Environment.Remove(key);
        startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, new[] { cli }.Concat(paths));
        startInfo.Environment["HERMES_LILITH_CLI_COMPONENT"] = component;
        startInfo.Environment["HERMES_LILITH_BOUND_HOME"] = Path.GetFullPath(options.HermesHomePath);
        startInfo.Environment["HERMES_LILITH_CLI_VOICE_SITE"] = Path.GetFullPath(options.VoiceSitePackagesPath);
        // Only explicitly approved, receipt-verified optional SDKs may be
        // activated from this bound profile; never inherit another home's path.
        startInfo.Environment["HERMES_LILITH_PYTHON_DEPENDENCIES_ROOT"] =
            Path.Combine(Path.GetFullPath(options.HermesHomePath), "tools", "dependencies");
        HermesPackagedToolsEnvironment.Configure(startInfo, options);
        return true;
    }
}
