using LilithRuntimeContracts;

namespace LilithControlCenter;

internal static class LocalCompatibilityProtocolGuard
{
    // A stale Runtime hello cannot re-enable the Owner-retired product feature.
    internal static bool Published(IEnumerable<string> methods) => false;
}

// Inert compatibility tombstone for callers compiled against the old interface.
// Never connect, inspect the stored preference, take a mutation gate, or send an
// RPC. The local provider API and ordinary Workbench lifecycle are independent.
public sealed partial class D046HermesWorkbenchRuntimePipeProjection : ILocalCompatibilityProjection
{
    public Task<LocalCompatibilitySnapshot> ReadLocalCompatibilityAsync(CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<LocalCompatibilitySnapshot>(cancellationToken)
            : Task.FromResult(new LocalCompatibilitySnapshot(false, null));

    public Task<HermesLocalCompatibilityCommit> SaveLocalCompatibilityAsync(HermesLocalCompatibilityUpdate request,
        CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<HermesLocalCompatibilityCommit>(cancellationToken)
            : Task.FromException<HermesLocalCompatibilityCommit>(
                new LocalCompatibilityOperationException("local_compatibility.retired"));
}
