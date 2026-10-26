namespace LilithRuntime;

internal static class HermesPinnedInstallation
{
    internal static async Task VerifyAsync(
        LilithRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        await HermesPortableComponentInstallation.VerifyAsync(
            options,
            cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildExpectedPatchSurface(
        IEnumerable<string> lockedPaths,
        IReadOnlySet<string> upstreamTrackedPaths)
        => string.Join(
            '\n',
            lockedPaths
                .OrderBy(path =>
                    upstreamTrackedPaths.Contains(path) ? 0 : 1)
                .ThenBy(static path => path, StringComparer.Ordinal)
                .Select(path =>
                    (upstreamTrackedPaths.Contains(path) ? " M " : "?? ")
                    + path));

    internal static bool PatchSurfaceMatches(
        string normalizedStatus,
        string expectedPatchSurface)
        => string.Equals(
            normalizedStatus,
            expectedPatchSurface,
            StringComparison.Ordinal);

}
