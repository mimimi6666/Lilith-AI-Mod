using System.IO;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection
    : ID046HermesCapabilityCatalogProjection, ID046HermesCommandCatalogProjection
{
    // Runtime assembles this read-only snapshot from six bounded Hermes reads.
    // Keep its aggregate deadline separate from the ordinary interactive RPC
    // deadline so a slow catalog cannot fault the shared Workbench projection.
    private static readonly TimeSpan CapabilityCatalogResponseTimeout =
        TimeSpan.FromSeconds(180);

    private string _capabilityCatalogHelloRuntimeVersion = string.Empty;
    private string _capabilityCatalogHelloHermesVersion = string.Empty;
    private string _capabilityCatalogHelloHermesCommit = string.Empty;

    public async Task<HermesCapabilityCatalog> GetCapabilityCatalogAsync(
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var protocol = _protocol
            as ID046HermesCapabilityCatalogRuntimeProtocol
            ?? throw new NotSupportedException(
                "目前無法查看能力目錄。");
        var binding = protocol.BindCapabilityCatalog();
        var catalog = await SendCatalogReadAsync(
            binding,
            (response, envelope) =>
            {
                var decoded = protocol.DecodeCapabilityCatalog(response, envelope, binding);
                ValidateCapabilityCatalogHelloIdentity(decoded.Identity);
                return decoded;
            },
            cancellationToken,
            responseTimeout: CapabilityCatalogResponseTimeout).ConfigureAwait(false);
        return catalog;
    }

    public async Task<HermesCommandCatalogSnapshot> GetCommandCatalogAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol
            ?? throw new NotSupportedException("Commands require the canonical native catalog.");
        var binding = protocol.BindCommandCatalog();
        var catalog = await SendCatalogReadAsync(binding,
            (response, envelope) =>
            {
                var decoded = protocol.DecodeCommandCatalog(response, envelope, binding);
                ValidateCapabilityCatalogHelloIdentity(decoded.Identity);
                return decoded;
            },
            cancellationToken).ConfigureAwait(false);
        return catalog;
    }

    private void CaptureCapabilityCatalogHelloIdentity(LilithRuntimeHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _capabilityCatalogHelloRuntimeVersion = hello.RuntimeVersion;
        _capabilityCatalogHelloHermesVersion = hello.HermesVersion;
        _capabilityCatalogHelloHermesCommit = hello.HermesCommit;
    }

    private void ValidateCapabilityCatalogHelloIdentity(
        HermesCapabilityCatalogIdentity identity)
    {
        lock (_stateGate)
        {
            if (!_helloReceived
                || !string.Equals(
                    identity.RuntimeVersion,
                    _capabilityCatalogHelloRuntimeVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.HermesVersion,
                    _capabilityCatalogHelloHermesVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    identity.HermesCommit,
                    _capabilityCatalogHelloHermesCommit,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes capability catalog identity did not match Runtime hello.");
            }
        }
    }
}
