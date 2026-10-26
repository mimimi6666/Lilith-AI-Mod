namespace LilithRuntime;

// Selection is not attestation. Only genuinely absent newer versions allow
// an older location; verification never retries after a failed closure.
internal static class PinnedHermesVoiceLayout
{
    internal const string LegacyVersion = "v1-win-x64-cp311-hermes-0.20.1";
    internal const int LegacyEntryCount = 2299;
    internal const int LegacyFileCount = 2076;
    internal const string LegacyClosureSha256 =
        "8CE2D9101EE8288068DFE6DF1E633AED11C8CD0E3004FA80329C18B8FEADC70F";
    internal const string PreviousVersion = "v2-win-x64-cp311-hermes-0.20.1";
    internal const int PreviousEntryCount = 2284;
    internal const int PreviousFileCount = 2062;
    internal const string PreviousClosureSha256 =
        "2F7DB87D3BDC268977410A2B87EC1F2FB444DAB4A927C7BDA1288402C58BE244";

    internal sealed record Identity(int EntryCount, int FileCount, string ClosureSha256);

    internal static string ResolveDefaultRootPath(
        string runtimeRoot,
        Func<string, FileAttributes?>? readAttributes = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot) || !Path.IsPathFullyQualified(runtimeRoot))
            throw new ArgumentException("The Runtime root must be absolute.", nameof(runtimeRoot));

        var parent = Path.Combine(Path.GetFullPath(runtimeRoot), "Components", "voice");
        var current = Path.Combine(parent, LilithRuntimeOptions.PinnedVoiceComponentVersion);
        var inspect = readAttributes ?? LilithRuntimeWorkspacePathGuard.ReadExistingAttributes;
        foreach (var version in new[] { LilithRuntimeOptions.PinnedVoiceComponentVersion, PreviousVersion, LegacyVersion })
        {
            var candidate = Path.Combine(parent, version);
            if (!IsAbsent(candidate, inspect)) return candidate;
        }
        return current;
    }

    internal static Identity IdentityForRootPath(string rootPath)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(name, LegacyVersion, comparison))
            return new Identity(LegacyEntryCount, LegacyFileCount, LegacyClosureSha256);
        if (string.Equals(name, PreviousVersion, comparison))
            return new Identity(PreviousEntryCount, PreviousFileCount, PreviousClosureSha256);
        return new Identity(LilithRuntimeOptions.PinnedVoiceComponentEntryCount,
                LilithRuntimeOptions.PinnedVoiceComponentFileCount,
                LilithRuntimeOptions.PinnedVoiceComponentClosureSha256);
    }

    private static bool IsAbsent(string path, Func<string, FileAttributes?> inspect)
    {
        try
        {
            return !inspect(path).HasValue;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unknown is not absent. Retain this root so the normal verifier
            // reports the failure within the guarded startup, without fallback.
            return false;
        }
    }
}
