namespace LilithRuntime;

internal static class LilithIdentityTransitionGuard
{
    internal const string PendingFileName = "identity-transition.pending.v1.json";

    // No automatic repair or deletion. An interrupted offline transaction must
    // be resolved against its exact preimages before native children can start.
    internal static void ThrowIfPending(string hermesHomePath)
    {
        var path = Path.Combine(hermesHomePath, "runtime", PendingFileName);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        if (File.Exists(path) || Directory.Exists(path))
            throw new LilithRuntimeException(
                "A Lilith identity transition is pending; startup refused before profile mutation. Resolve its verified transaction first.");
    }
}
