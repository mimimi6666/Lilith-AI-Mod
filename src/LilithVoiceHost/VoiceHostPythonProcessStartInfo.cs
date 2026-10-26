using System.Diagnostics;
using System.Text;

namespace LilithVoiceHost;

internal static class VoiceHostPythonProcessStartInfo
{
    internal static ProcessStartInfo Create(
        string python,
        string api,
        int port,
        string config,
        string workingDirectory,
        string? requestedDevice = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"\"{api}\" -a 127.0.0.1 -p {port} -c \"{config}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Set only this child's standard streams, not Python's default file encoding.
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        if (requestedDevice != null)
        {
            if (requestedDevice is not ("cuda" or "cpu" or "auto"))
                throw new ArgumentException("Unsupported voice device preference.", nameof(requestedDevice));
            using var resource = typeof(VoiceHostPythonProcessStartInfo).Assembly
                .GetManifestResourceStream("LilithVoiceHost.managed_voice_startup.py")
                ?? throw new InvalidOperationException("Managed voice startup resource is missing.");
            using var reader = new StreamReader(resource, Encoding.UTF8);
            startInfo.Arguments = string.Empty;
            foreach (var argument in new[]
            {
                "-B", "-u", "-c", reader.ReadToEnd(), api,
                port.ToString(System.Globalization.CultureInfo.InvariantCulture), config, requestedDevice
            })
                startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }
}
