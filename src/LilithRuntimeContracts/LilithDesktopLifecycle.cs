using System.Security.Cryptography;
using System.Text;

namespace LilithRuntimeContracts;

// Local, per-user lifecycle signalling only. No model/tool/settings authority.
public static class LilithDesktopLifecycle
{
    public static string SignalName(string purpose, string userIdentity, string executable)
    {
        if (purpose is not ("runtime-exit" or "control-center-show" or "control-center-instance"))
            throw new ArgumentException("Unknown lifecycle signal.");
        if (string.IsNullOrWhiteSpace(userIdentity) || !Path.IsPathFullyQualified(executable))
            throw new ArgumentException("Lifecycle identity must be explicit.");
        var scope = userIdentity + "\n" + Path.GetFullPath(executable).ToUpperInvariant();
        return "Local\\Lilith." + purpose + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..32];
    }
}
